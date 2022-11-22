﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2013-2020.

namespace OmniBlade
open System
open System.Numerics
open Prime
open Nu
open Nu.Declarative
open OmniBlade

[<RequireQualifiedAccess>]
module FieldCue =

    let rec run
        (cue : Cue)
        (definitions : CueDefinitions)
        (field : Field) :
        Cue * CueDefinitions * (Signal<FieldMessage, FieldCommand> list * Field) =

        match cue with
        | Cue.Nil ->
            (cue, definitions, just field)

        | Cue.PlaySound (volume, sound) ->
            (Cue.Nil, definitions, withCmd (PlaySound (0L, volume, sound)) field)

        | Cue.PlaySong (fadeIn, fadeOut, volume, start, song) ->
            (Cue.Nil, definitions, withCmd (PlaySong (fadeIn, fadeOut, volume, start, song)) field)

        | Cue.FadeOutSong fade ->
            (Cue.Nil, definitions, withCmd (FadeOutSong fade) field)

        | Cue.Face (target, direction) ->
            match target with
            | AvatarTarget ->
                let field = Field.updateAvatar (Avatar.updateDirection (constant direction)) field
                (Cue.Nil, definitions, just field)
            | CharacterTarget characterType ->
                let propIdOpt =
                    Field.tryGetPropIdByData
                        (function
                         | Character (characterType2, _, _, _, _, requirements) -> characterType = characterType2 && field.Advents.IsSupersetOf requirements
                         | _ -> false)
                        field
                match propIdOpt with
                | Some propId ->
                    let field =
                        Field.updatePropState
                            (function
                             | CharacterState (color, animationState) ->
                                let animationState = CharacterAnimationState.face direction animationState
                                CharacterState (color, animationState)
                             | propState -> propState)
                            propId
                            field
                    (Cue.Nil, definitions, just field)
                | None ->
                    (Cue.Nil, definitions, just field)
            | NpcTarget _ | ShopkeepTarget _ | CharacterIndexTarget _ | SpriteTarget _ ->
                (Cue.Nil, definitions, just field)

        | Cue.ClearSpirits ->
            let field = Field.clearSpirits field
            (Cue.Nil, definitions, just field)

        | Recruit allyType ->
            let fee = Field.getRecruitmentFee field
            if field.Inventory.Gold >= fee then
                let advent =
                    match allyType with
                    | Jinn -> failwithumf ()
                    | Shade -> ShadeRecruited
                    | Mael -> MaelRecruited
                    | Riain -> RiainRecruited
                    | Peric -> PericRecruited
                let field = Field.recruit allyType field
                let field = Field.updateAdvents (Set.add advent) field
                let field = Field.updateInventory (Inventory.updateGold (fun gold -> gold - fee)) field
                (Cue.Nil, definitions, withCmd (PlaySound (0L, Constants.Audio.SoundVolumeDefault, Assets.Field.PurchaseSound)) field)
            else run (Parallel [Dialog ("You don't have enough...", false); Cue.PlaySound (Constants.Audio.SoundVolumeDefault, Assets.Gui.MistakeSound)]) definitions field

        | AddItem itemType ->
            (Cue.Nil, definitions, just (Field.updateInventory (Inventory.tryAddItem itemType >> snd) field))

        | RemoveItem itemType ->
            (Cue.Nil, definitions, just (Field.updateInventory (Inventory.tryRemoveItem itemType >> snd) field))

        | AddAdvent advent ->
            (Cue.Nil, definitions, just (Field.updateAdvents (Set.add advent) field))

        | RemoveAdvent advent ->
            (Cue.Nil, definitions, just (Field.updateAdvents (Set.remove advent) field))

        | ReplaceAdvent (remove, add) ->
            (Cue.Nil, definitions, just (Field.updateAdvents (Set.remove remove >> Set.add add) field))

        | Wait time ->
            (WaitState (field.UpdateTime + time), definitions, just field)

        | WaitState time ->
            if field.UpdateTime < time
            then (cue, definitions, just field)
            else (Cue.Nil, definitions, just field)

        | Fade (target, length, fadeIn) ->
            (FadeState (field.UpdateTime, target, length, fadeIn), definitions, just field)

        | FadeState (startTime, target, length, fadeIn) ->
            let time = field.UpdateTime
            let localTime = time - startTime
            let progress = single localTime / single length
            let progress = if fadeIn then progress else 1.0f - progress
            let field =
                match target with
                | CharacterTarget characterType ->
                    let propIdOpt =
                        Field.tryGetPropIdByData
                            (function
                             | Character (characterType2, _, _, _, _, requirements) -> characterType = characterType2 && field.Advents.IsSupersetOf requirements
                             | _ -> false)
                            field
                    match propIdOpt with
                    | Some propId ->
                        Field.updatePropState
                            (function
                             | CharacterState (_, animationState) -> CharacterState (Color.One.MapA ((*) progress), animationState)
                             | propState -> propState)
                            propId
                            field
                    | None -> field
                | SpriteTarget spriteName ->
                    match Field.tryGetPropIdByData (function Sprite (spriteName2, _, _, _, _, _, _) -> spriteName = spriteName2 | _ -> false) field with
                    | Some propId ->
                        Field.updateProp
                            (fun prop ->
                                match prop.PropData with
                                | Sprite (_, _, color, _, _, _, _) ->
                                    Prop.updatePropState
                                        (function
                                         | SpriteState (image, _, blend, glow, flip, _) -> SpriteState (image, color.MapA ((*) progress), blend, glow, flip, true)
                                         | propState -> propState)
                                        prop
                                | _ -> prop)
                            propId
                            field
                    | None -> field
                | _ -> field
            if  fadeIn && progress >= 1.0f ||
                not fadeIn && progress <= 0.0f then
                (Cue.Nil, definitions, just field)
            else (cue, definitions, just field)

        | Animate (target, characterAnimationType, wait) ->
            match target with
            | AvatarTarget ->
                let field = Field.updateAvatar (Avatar.animate field.UpdateTime characterAnimationType) field
                match wait with
                | Timed 0L | NoWait -> (Cue.Nil, definitions, just field)
                | CueWait.Wait | Timed _ -> (AnimateState (field.UpdateTime, wait), definitions, just field)
            | CharacterTarget characterType ->
                let propIdOpt =
                    Field.tryGetPropIdByData
                        (function
                         | Character (characterType2, _, _, _, _, requirements) -> characterType = characterType2 && field.Advents.IsSupersetOf requirements
                         | _ -> false)
                        field
                match propIdOpt with
                | Some propId ->
                    let field =
                        Field.updatePropState
                            (function
                             | CharacterState (color, animationState) ->
                                let animationState = CharacterAnimationState.setCharacterAnimationType (Some field.UpdateTime) characterAnimationType animationState
                                CharacterState (color, animationState)
                             | propState -> propState)
                            propId
                            field
                    (Cue.Nil, definitions, just field)
                | None ->
                    (Cue.Nil, definitions, just field)
            | NpcTarget _ | ShopkeepTarget _ | CharacterIndexTarget _ | SpriteTarget _ ->
                (Cue.Nil, definitions, just field)

        | AnimateState (startTime, wait) ->
            let time = field.UpdateTime
            match wait with
            | CueWait.Wait ->
                if Avatar.getAnimationFinished time field.Avatar
                then (Cue.Nil, definitions, just field)
                else (cue, definitions, just field)
            | Timed waitTime ->
                let localTime = time - startTime
                if localTime < waitTime
                then (cue, definitions, just field)
                else (Cue.Nil, definitions, just field)
            | NoWait ->
                (Cue.Nil, definitions, just field)

        | Move (target, destination, moveType) ->
            match target with
            | AvatarTarget ->
                let cue = MoveState (field.UpdateTime, target, field.Avatar.Bottom, destination, moveType)
                (cue, definitions, just field)
            | CharacterTarget characterType ->
                let propIdOpt =
                    Field.tryGetPropIdByData
                        (function
                         | Character (characterType2, _, _, _, _, requirements) -> characterType = characterType2 && field.Advents.IsSupersetOf requirements
                         | _ -> false)
                        field
                match propIdOpt with
                | Some propId ->
                    let prop = field.Props.[propId]
                    let cue = MoveState (field.UpdateTime, target, prop.Perimeter.Bottom, destination, moveType)
                    (cue, definitions, just field)
                | None -> (Cue.Nil, definitions, just field)
            | NpcTarget _ | ShopkeepTarget _ | CharacterIndexTarget _ | SpriteTarget _ ->
                (Cue.Nil, definitions, just field)

        | MoveState (startTime, target, origin, translation, moveType) ->
            match target with
            | AvatarTarget ->
                let time = field.UpdateTime
                let localTime = time - startTime
                let (step, stepCount) = MoveType.computeStepAndStepCount translation moveType
                let totalTime = int64 (dec stepCount)
                if localTime < totalTime then
                    let field = Field.updateAvatar (Avatar.updateBottom ((+) step)) field
                    (cue, definitions, just field)
                else
                    let field = Field.updateAvatar (Avatar.updateBottom (constant (origin + translation))) field
                    (Cue.Nil, definitions, just field)
            | CharacterTarget characterType ->
                let propIdOpt =
                    Field.tryGetPropIdByData
                        (function
                         | Character (characterType2, _, _, _, _, requirements) -> characterType = characterType2 && field.Advents.IsSupersetOf requirements
                         | _ -> false)
                        field
                match propIdOpt with
                | Some propId ->
                    let prop = field.Props.[propId]
                    let time = field.UpdateTime
                    let localTime = time - startTime
                    let (step, stepCount) = MoveType.computeStepAndStepCount translation moveType
                    let finishTime = int64 (dec stepCount)
                    if localTime < finishTime then
                        let bounds = prop.Perimeter.Translate step
                        let field = Field.updateProp (Prop.updatePerimeter (constant bounds)) propId field
                        (cue, definitions, just field)
                    else
                        let bounds = prop.Perimeter.WithBottom (origin + translation)
                        let field = Field.updateProp (Prop.updatePerimeter (constant bounds)) propId field
                        (Cue.Nil, definitions, just field)
                | None -> (Cue.Nil, definitions, just field)
            | NpcTarget _ | ShopkeepTarget _ | CharacterIndexTarget _ | SpriteTarget _ ->
                (Cue.Nil, definitions, just field)

        | Warp (fieldType, fieldDestination, fieldDirection) ->
            match field.FieldTransitionOpt with
            | Some _ ->
                (cue, definitions, just field)
            | None ->
                let fieldTransition =
                    { FieldType = fieldType
                      FieldDestination = fieldDestination
                      FieldDirection = fieldDirection
                      FieldTransitionTime = field.UpdateTime + Constants.Field.TransitionTime }
                let field = Field.updateFieldTransitionOpt (constant (Some fieldTransition)) field
                (WarpState, definitions, just field)

        | WarpState ->
            match field.FieldTransitionOpt with
            | Some _ -> (cue, definitions, just field)
            | None -> (Cue.Nil, definitions, just field)

        | Battle (battleType, consequents) ->
            match field.BattleOpt with
            | Some _ -> (cue, definitions, just field)
            | None -> (BattleState, definitions, withMsg (TryBattle (battleType, consequents)) field)

        | BattleState ->
            match field.BattleOpt with
            | Some _ -> (cue, definitions, just field)
            | None -> (Cue.Nil, definitions, just field)

        | Dialog (text, isNarration) ->
            match field.DialogOpt with
            | Some _ ->
                (cue, definitions, just field)
            | None ->
                let dialogForm = if isNarration then DialogNarration else DialogThick
                let dialog = { DialogForm = dialogForm; DialogTokenized = text; DialogProgress = 0; DialogPage = 0; DialogPromptOpt = None; DialogBattleOpt = None }
                let field = Field.updateDialogOpt (constant (Some dialog)) field
                (DialogState, definitions, just field)

        | DialogState ->
            match field.DialogOpt with
            | None -> (Cue.Nil, definitions, just field)
            | Some _ -> (cue, definitions, just field)

        | Prompt (text, leftPrompt, rightPrompt) ->
            match field.DialogOpt with
            | Some _ ->
                (cue, definitions, just field)
            | None ->
                let dialog = { DialogForm = DialogThick; DialogTokenized = text; DialogProgress = 0; DialogPage = 0; DialogPromptOpt = Some (leftPrompt, rightPrompt); DialogBattleOpt = None }
                let field = Field.updateDialogOpt (constant (Some dialog)) field
                (PromptState, definitions, just field)

        | PromptState ->
            match field.DialogOpt with
            | None -> (Cue.Nil, definitions, just field)
            | Some _ -> (cue, definitions, just field)

        | If (p, c, a) ->
            match p with
            | Gold gold -> if field.Inventory.Gold >= gold then (c, definitions, just field) else (a, definitions, just field)
            | Item itemType -> if Inventory.containsItem itemType field.Inventory then (c, definitions, just field) else (a, definitions, just field)
            | Items itemTypes -> if Inventory.containsItems itemTypes field.Inventory then (c, definitions, just field) else (a, definitions, just field)
            | Advent advent -> if field.Advents.Contains advent then (c, definitions, just field) else (a, definitions, just field)
            | Advents advents -> if field.Advents.IsSupersetOf advents then (c, definitions, just field) else (a, definitions, just field)

        | Not (p, c, a) ->
            match p with
            | Gold gold -> if field.Inventory.Gold < gold then (c, definitions, just field) else (a, definitions, just field)
            | Item itemType -> if not (Inventory.containsItem itemType field.Inventory) then (c, definitions, just field) else (a, definitions, just field)
            | Items itemTypes -> if not (Inventory.containsItems itemTypes field.Inventory) then (c, definitions, just field) else (a, definitions, just field)
            | Advent advent -> if not (field.Advents.Contains advent) then (c, definitions, just field) else (a, definitions, just field)
            | Advents advents -> if not (field.Advents.IsSupersetOf advents) then (c, definitions, just field) else (a, definitions, just field)

        | Define (name, body) ->
            if not (Map.containsKey name definitions) then
                (Cue.Nil, Map.add name body definitions, just field)
            else
                Log.debug ("Cue definition '" + name + "' already found.")
                (Cue.Nil, definitions, just field)

        | Assign (name, body) ->
            if Map.containsKey name definitions then
                (Cue.Nil, Map.add name body definitions, just field)
            else
                Log.debug ("Cue definition '" + name + "' not found.")
                (Cue.Nil, definitions, just field)

        | Expand name ->
            match Map.tryFind name definitions with
            | Some body ->
                run body definitions field
            | None ->
                Log.debug ("Cue definition '" + name + "' not found.")
                (Cue.Nil, definitions, ([], field))

        | Parallel cues ->
            let (cues, definitions, (signals, field)) =
                List.fold (fun (cues, definitions, (signals, field)) cue ->
                    let (cue, definitions, (signals2, field)) = run cue definitions field
                    if Cue.isNil cue
                    then (cues, definitions, (signals @ signals2, field))
                    else (cues @ [cue], definitions, (signals @ signals2, field)))
                    ([], definitions, ([], field))
                    cues
            match cues with
            | _ :: _ -> (Parallel cues, definitions, (signals, field))
            | [] -> (Cue.Nil, definitions, (signals, field))

        | Sequence cues ->
            let (_, haltedCues, definitions, (signals, field)) =
                List.fold (fun (halted, haltedCues, definitions, (signals, field)) cue ->
                    if halted
                    then (halted, haltedCues @ [cue], definitions, (signals, field))
                    else
                        let (cue, definitions, (signals2, field)) = run cue definitions field
                        if Cue.isNil cue
                        then (false, [], definitions, (signals @ signals2, field))
                        else (true, [cue], definitions, (signals @ signals2, field)))
                    (false, [], definitions, ([], field))
                    cues
            match haltedCues with
            | _ :: _ -> (Sequence haltedCues, definitions, (signals, field))
            | [] -> (Cue.Nil, definitions, (signals, field))