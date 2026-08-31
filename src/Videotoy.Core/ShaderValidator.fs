module Videotoy.Core.ShaderValidator

open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel

let private mainImageRegex =
    Regex(@"void\s+mainImage\s*\(", RegexOptions.Compiled)

let private versionDirectiveRegex =
    Regex(@"^\s*#version\b", RegexOptions.Compiled)

let private findUnbalancedDelimiterLine (sourceLines: string[]) (openChar: char) (closeChar: char) : int option =
    let mutable depth = 0
    let mutable firstNegativeLine = None

    for lineIndex in 0 .. sourceLines.Length - 1 do
        for ch in sourceLines.[lineIndex] do
            if ch = openChar then
                depth <- depth + 1
            elif ch = closeChar then
                depth <- depth - 1
                if depth < 0 && firstNegativeLine.IsNone then
                    firstNegativeLine <- Some(lineIndex + 1)

    if depth > 0 then Some(sourceLines.Length)
    else firstNegativeLine

let validatePass (pass: ShaderPass) : ShaderIssue list =
    let sourceLines = pass.SourceCode.Replace("\r\n", "\n").Split('\n')
    let issues = ResizeArray<ShaderIssue>()

    if not (mainImageRegex.IsMatch(pass.SourceCode)) then
        issues.Add(errorIssue pass.Name 1 "Missing 'mainImage' entry point.")

    match findUnbalancedDelimiterLine sourceLines '{' '}' with
    | Some line -> issues.Add(errorIssue pass.Name line "Unbalanced braces '{' / '}'.")
    | None -> ()

    match findUnbalancedDelimiterLine sourceLines '(' ')' with
    | Some line -> issues.Add(errorIssue pass.Name line "Unbalanced parentheses '(' / ')'.")
    | None -> ()

    sourceLines
    |> Array.iteri (fun index line ->
        if versionDirectiveRegex.IsMatch(line) then
            issues.Add(warningIssue pass.Name (index + 1) "Stray '#version' directive: not required for Shadertoy passes."))

    issues |> List.ofSeq

let validateProject (project: ShaderProject) : ShaderIssue list =
    allPasses project
    |> List.collect validatePass
