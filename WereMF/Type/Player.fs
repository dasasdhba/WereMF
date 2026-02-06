module WereMF.Type.Player

type PlayerId =
    | PlayerId of int
    member this.ToInt() =
        let (PlayerId id) = this
        id
    member this.ToCircleString() =
        let circles = "①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳㉑㉒㉓㉔㉕㉖㉗㉘㉙㉚㉛㉜㉝㉞㉟㊱㊲㊳㊴㊵㊶㊷㊸㊹㊺㊻㊼㊽㊾㊿"
        if this > PlayerId 0 && this <= PlayerId 50 then
            string circles[this.ToInt() - 1]
        else
            this.ToString()
    override this.ToString() =
        this.ToInt().ToString()

type Player =
    {
        Id : PlayerId
        Name : string
    }
    member this.ToCliString() =
        $"{this.Id.ToString()}: {this.Name}"
    member this.ToAliveString() =
        $"{this.Id.ToCircleString()} {this.Name}"