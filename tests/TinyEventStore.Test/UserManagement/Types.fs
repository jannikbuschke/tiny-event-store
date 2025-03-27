namespace UserManagement


type UserEvent =
  | Created of
    {| Name: string
       Password: string
       Hash: byte array
       Salt: byte array |}
  | PasswordChanged of {| Password: string |}
  | Deleted

type UserId = UserId of string
type GroupId = GroupId of string

type Member =
  | UserId of UserId
  | GroupId of GroupId

type GroupEvent =
  | Created of string
  | MemberAdded of Member
  | MemberRemoved of Member


type MemberShip =
  | DirectMembership of GroupId
  | IndirectMembership of GroupId

type MemberShips = MemberShip list

type GroupMember =
  | DirectMember of GroupMember
  | IndirectMember of GroupMember

type GroupMembers = MemberShip list
