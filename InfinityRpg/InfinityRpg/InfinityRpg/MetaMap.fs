﻿namespace InfinityRpg
open System
open OpenTK
open TiledSharp
open Prime
open Nu

type Tracking =
    | BackTracking
    | NoBackTracking
    | NoAdjacentTracking

module MetamapMetrics =

    let mutable GlobalWalkCounter = 0
    let mutable GlobalTryStumbleCounter = 0
    let mutable GlobalWanderCounter = 0

[<AutoOpen>]
module DirectionModule =

    type Direction with

        static member next rand =
            let randMax = 4
            let (randValue, rand) = Rand.nextIntUnder randMax rand
            let direction = Direction.fromInt randValue
            (direction, rand)

        static member walk (source : Vector2i) direction =
            MetamapMetrics.GlobalWalkCounter <- MetamapMetrics.GlobalWalkCounter + 1
            match direction with
            | Upward -> Vector2i (source.X, source.Y + 1)
            | Rightward -> Vector2i (source.X + 1, source.Y)
            | Downward -> Vector2i (source.X, source.Y - 1)
            | Leftward -> Vector2i (source.X - 1, source.Y)

        static member private stumbleUnbiased source rand =
            let (direction, rand) = Direction.next rand
            let destination = Direction.walk source direction
            (destination, rand)

        static member stumble optBias source rand =
            match optBias with
            | Some (goal : Vector2i, bias) ->
                let (biasing, rand) = Rand.nextIntUnder bias rand
                if biasing = 0 then
                    let goalDelta = goal - source
                    if Math.Abs goalDelta.X > Math.Abs goalDelta.Y
                    then (Vector2i (source.X + (if goalDelta.X > 0 then 1 else -1), source.Y), rand)
                    else (Vector2i (source.X, source.Y + (if goalDelta.Y > 0 then 1 else -1)), rand)
                else Direction.stumbleUnbiased source rand
            | None -> Direction.stumbleUnbiased source rand

        static member tryStumbleUntil predicate tryLimit optBias source rand =
            MetamapMetrics.GlobalTryStumbleCounter <- MetamapMetrics.GlobalTryStumbleCounter + 1
            let destinations =
                Seq.unfold
                    (fun rand ->
                        let (destination, rand) = Direction.stumble optBias source rand
                        Some ((destination, rand), rand))
                    rand
            let destinations = if tryLimit <= 0 then destinations else Seq.take tryLimit destinations
            let destinations = Seq.tryFind predicate destinations
            destinations

        static member wander stumbleLimit stumbleBounds tracking optBias source rand =
            MetamapMetrics.GlobalWanderCounter <- MetamapMetrics.GlobalWanderCounter + 1
            let stumblePredicate =
                fun (trail : Vector2i Set) (destination : Vector2i, _) ->
                    MapBounds.isPointInBounds destination stumbleBounds &&
                    (match tracking with
                     | BackTracking -> true
                     | NoBackTracking -> not <| Set.contains destination trail
                     | NoAdjacentTracking ->
                        let contains =
                            [Set.contains (destination + Vector2i.Up) trail
                             Set.contains (destination + Vector2i.Right) trail
                             Set.contains (destination + Vector2i.Down) trail
                             Set.contains (destination + Vector2i.Left) trail]
                        let containCount = List.filter ((=) true) contains |> List.length
                        containCount <= 1)
            let pathHead = (source, rand)
            let pathTail =
                Seq.unfold
                    (fun (trail, source, rand) ->
                        match Direction.tryStumbleUntil (stumblePredicate trail) stumbleLimit optBias source rand with
                        | Some (destination, rand) ->
                            let state = (Set.add destination trail, destination, rand)
                            Some ((destination, rand), state)
                        | None -> None)
                    (Set.singleton source, source, rand)
            let path = seq { yield pathHead; yield! pathTail }
            path

        static member tryWanderUntil predicate stumbleLimit stumbleBounds tracking optBias tryLimit source rand =
            let paths =
                Seq.unfold
                    (fun (source, rand) ->
                        let path = Direction.wander stumbleLimit stumbleBounds tracking optBias source rand
                        let state = (source, Rand.advance rand)
                        Some (path, state))
                    (source, rand)
            let paths = if tryLimit <= 0 then paths else Seq.take tryLimit paths
            let paths = Seq.tryFind predicate paths
            paths

        static member wanderUntil predicate stumbleLimit stumbleBounds tracking optBias source rand =
            Option.get <| Direction.tryWanderUntil predicate stumbleLimit stumbleBounds tracking optBias 0 source rand

        static member concretizePath maxLength abstractPath =
            let path = List.ofSeq <| Seq.tryTake maxLength abstractPath
            (List.map fst path, snd <| List.last path)

        static member concretizeOptPath maxLength abstractPath rand =
            match abstractPath with
            | Some path ->
                let (path, rand) = Direction.concretizePath maxLength path
                (Some path, rand)
            | None -> (None, rand)

        static member wanderAimlessly stumbleBounds source rand =
            let minLength = 10
            let maxLength = 15
            let tryLimit = 100
            let stumbleLimit = 16
            let predicate = fun (path : (Vector2i * Rand) seq) ->
                let path = List.ofSeq <| Seq.tryTake maxLength path
                if List.length path >= minLength then
                    let sites = List.map fst path
                    let uniqueSites = Set.ofList sites
                    List.length sites = Set.count uniqueSites
                else false
            let path = Direction.tryWanderUntil predicate stumbleLimit stumbleBounds BackTracking None tryLimit source rand
            let path = Direction.concretizeOptPath maxLength path rand
            path

        static member wanderToDestination stumbleBounds source destination rand =
            let optBias = Some (destination, 6)
            let maxPathLength = stumbleBounds.CornerPositive.X * stumbleBounds.CornerPositive.Y / 2 + 1
            let stumbleLimit = 16
            let predicate = fun path ->
                let path = Seq.tryTake maxPathLength path
                Seq.exists (fun point -> fst point = destination) path
            let path = Direction.wanderUntil predicate stumbleLimit stumbleBounds NoAdjacentTracking optBias source rand
            let pathDesiredEnd = Seq.findIndex (fun (point, _) -> point = destination) path + 1
            let pathTrimmed = Seq.take pathDesiredEnd path
            let path = Direction.concretizePath maxPathLength pathTrimmed
            path

    type MetaTile<'k when 'k : comparison> =
        { ClosedSides : Direction Set
          LockedSides : Map<Direction, 'k>
          Keys : 'k Set }

    type MetaMap<'k when 'k : comparison>  =
        { NavigableSize : Vector2i
          PotentiallyNavigableTiles : Map<Vector2i, 'k MetaTile> }