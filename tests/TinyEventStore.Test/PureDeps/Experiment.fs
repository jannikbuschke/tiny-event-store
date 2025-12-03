module TinyEventStore.Test.PureDeps.Experiment

open System.Threading.Tasks

type Request =
  | One of string
  | Two of int

 type Command =
   | CommandOne of string * string
   | CommandTwo of int * string list


let prepare1 (v:string)=
  task{return ""}
let prepare2 (v:int)=
  task{return [""]}


let handle1 (v:string,ctx:string)=
  ()

let handle2 (v:int,ctx:string list)=
  ()

open FsToolkit.ErrorHandling
let cmd1 (input:string)= input |> prepare1 |> Task.map (fun x -> CommandOne(input,x)) |> Task.map (fun x -> handle1 x)

let pipe (v:Request)=
  match v with
  | One v -> prepare1 v |> Task.map (fun x -> CommandOne(v,x))
  | Two v -> prepare2 v |> Task.map (fun x -> CommandTwo(v,x))

let handleCmd = function
  | CommandOne(v1,v2)->handle1(v1,v2)
  | CommandTwo(v1,v2)->handle2(v1,v2)


let webHandler =
  pipe >> Task.map handleCmd
