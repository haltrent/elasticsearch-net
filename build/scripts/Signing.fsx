﻿#I @"../../packages/build/FAKE/tools"
#r @"FakeLib.dll"
#load @"Paths.fsx"

open System
open Fake 
    
open Paths
open Projects

module Sign = 
    let private sn = if isMono then "sn" else Paths.CheckedInTool("sn/sn.exe")
    let private keyFile =  Paths.Keys("keypair.snk");
    let private oficialToken = "96c599bbe3e70f5d"

    let private validate dll name = 
        let out = (ExecProcessAndReturnMessages(fun p ->
                    p.FileName <- sn
                    p.Arguments <- sprintf @"-v %s" dll
                  ) (TimeSpan.FromMinutes 5.0))
        
        let valid = (out.ExitCode, out.Messages.FindIndex(fun s -> s.Contains("is valid")))
        match valid with
        | (0, i) when i >= 0 -> trace (sprintf "%s was signed correctly" name) 
        | (_, _) -> failwithf "{0} was not validly signed"
        
        let out = (ExecProcessAndReturnMessages(fun p ->
                    p.FileName <- sn
                    p.Arguments <- sprintf @"-T %s" dll
                  ) (TimeSpan.FromMinutes 5.0))
        
        let tokenMessage = (out.Messages.Find(fun s -> s.Contains("Public key token is")));
        let token = (tokenMessage.Replace("Public key token is", "")).Trim();
    
        let valid = (out.ExitCode, token)
        match valid with
        | (0, t) when t = oficialToken  -> 
          trace (sprintf "%s was signed with official key token %s" name t) 
        | (_, t) -> traceFAKE "%s was not signed with the official token: %s but %s" name oficialToken t
        
    let ValidateNugetDllAreSignedCorrectly() = 
        for p in DotNetProject.AllPublishable do
            for f in DotNetFramework.All do 
                let name = p.Name
                let outputFolder = Paths.ProjectOutputFolder p f
                let dll = sprintf "%s/%s.dll" outputFolder name
                if fileExists dll then validate dll name
