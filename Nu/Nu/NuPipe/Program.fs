﻿namespace NuPipe
open System
open System.IO
open Prime
open Nu
open Nu.NuConstants
module Program =

    let [<EntryPoint>] main argv =
        match argv with
        | [|inputDir; outputDir|] ->
            match Assets.tryBuildAssetGraph inputDir outputDir AssetGraphFileName with
            | Left error ->
                Console.WriteLine error
                FailureReturnCode
            | Right () -> SuccessReturnCode
        | _ ->
            Console.WriteLine "NuPipe.exe requires two parameters (asset graph file name, input directory, and output directory)."
            FailureReturnCode