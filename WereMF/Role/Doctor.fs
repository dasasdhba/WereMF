module WereMF.Role.Doctor

open WereMF.Common

type DoctorRole =
    {
        Capsule : int
    }
    static member New () = { Capsule = 4 }
    interface IRole with
        member this.Base = {
            CharaType = Doctor
            Priority = 0
            SummaryName = Doctor.ToString ()
        }
