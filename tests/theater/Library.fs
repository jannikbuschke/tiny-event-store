module theater

open System

type RehearsalId = string
type ShowId = string
type PersonId = string
type RoleId = string
type ProductionId = string
type SceneId = string
type CastId = string
type GroupId = string

type Appearance =
  { From: TimeSpan
    Until: TimeSpan
    RoleId: RoleId }

type Scene =
  { Id: SceneId
    Name: string
    Appearance: Appearance list }

type Production =
  { Id: ProductionId; Scenes: Scene list }

type Cast =
  { CastId: CastId
    PersonId: PersonId
    RoleId: RoleId }

type Schedule =
  { Start: DateTimeOffset
    End: DateTimeOffset }


type Event =
  | RehearsalPlanned of
    {| Schedule: Schedule
       Cast: Cast list
       Scenes: Scene list |}
  | CastAdded of Cast

type Rehearsal =
  { Schedule: Schedule
    Cast: Cast list
    Scenes: Scene list }

type Version = int

type ActivityId =
  | RehearsalId of RehearsalId
  | ShowId of ShowId

type ActivityAt = Version * ActivityId

type PrincipalId =
  | PersonId of PersonId
  | GroupId of GroupId

type DispoEvent =
  | Created of DateOnly
  | SharedWith of PrincipalId list

type Dispo =
  { Date: DateOnly
    Activities: ActivityAt list
    SharedWith: PrincipalId list }
