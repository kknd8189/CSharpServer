using System;
using System.Collections.Generic;
using System.Text;
using System.Buffers.Binary;
using ServerCore;

namespace Protocol
{

public enum MsgId
{
    S_EnterGame = 0,
    S_LeaveGame = 1,
    S_Spawn = 2,
    S_Despawn = 3,
    C_Move = 4,
    S_Move = 5,
    C_Skill = 6,
    S_Skill = 7,
    S_ChangeHp = 8,
    S_Die = 9,
    S_Connected = 10,
    C_Login = 11,
    S_Login = 12,
    C_EnterGame = 13,
    C_CreatePlayer = 14,
    S_CreatePlayer = 15,
    S_ItemList = 16,
    S_AddItem = 17,
    C_EquipItem = 18,
    S_EquipItem = 19,
    S_ChangeStat = 20,
    S_Ping = 21,
    C_Pong = 22,

}


public enum CreatureState
{
    Idle = 0,
    Moving = 1,
    Skill = 2,
    Dead = 3,

}


public enum MoveDir
{
    Up = 0,
    Down = 1,
    Left = 2,
    Right = 3,
    Forward = 4,
    Backward = 5,

}


public enum GameObjectType
{
    None = 0,
    Player = 1,
    Monster = 2,
    Projectile = 3,

}


public enum SkillType
{
    None = 0,
    Auto = 1,
    Projectile = 2,

}


public enum PlayerServerState
{
    ServerStateLogin = 0,
    ServerStateLobby = 1,
    ServerStateGame = 2,

}


public enum ItemType
{
    None = 0,
    Weapon = 1,
    Armor = 2,
    Consumable = 3,

}


public enum WeaponType
{
    None = 0,
    Sword = 1,
    Bow = 2,

}


public enum ArmorType
{
    None = 0,
    Helmet = 1,
    Armor = 2,
    Boots = 3,

}


public enum ConsumableType
{
    None = 0,
    Potion = 1,

}


public class LobbyPlayerInfo
{
    public int PlayerDbId;
    public string Name;
    public StatInfo StatInfo;

    public void Read(ReadOnlySpan<byte> span, ref ushort count)
    {
        this.PlayerDbId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        ushort NameLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Name = Encoding.UTF8.GetString(span.Slice(count, NameLen)); count += NameLen;
        byte StatInfoHasValue = span[count]; count += sizeof(byte);
        if (StatInfoHasValue != 0)
        {
            this.StatInfo = new StatInfo();
            this.StatInfo.Read(span, ref count);
        }

    }

    public void Write(Span<byte> span, ref ushort count)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.PlayerDbId); count += sizeof(int);
        ushort NameLen = (ushort)(this.Name != null ? Encoding.UTF8.GetByteCount(this.Name) : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), NameLen); count += sizeof(ushort);
        if (this.Name != null)
        {
            Encoding.UTF8.GetBytes(this.Name, span.Slice(count)); count += NameLen;
        }
        if (this.StatInfo != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.StatInfo.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

    }
}


public class ObjectInfo
{
    public int ObjectId;
    public string Name;
    public PositionInfo PosInfo;
    public StatInfo StatInfo;

    public void Read(ReadOnlySpan<byte> span, ref ushort count)
    {
        this.ObjectId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        ushort NameLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Name = Encoding.UTF8.GetString(span.Slice(count, NameLen)); count += NameLen;
        byte PosInfoHasValue = span[count]; count += sizeof(byte);
        if (PosInfoHasValue != 0)
        {
            this.PosInfo = new PositionInfo();
            this.PosInfo.Read(span, ref count);
        }
        byte StatInfoHasValue = span[count]; count += sizeof(byte);
        if (StatInfoHasValue != 0)
        {
            this.StatInfo = new StatInfo();
            this.StatInfo.Read(span, ref count);
        }

    }

    public void Write(Span<byte> span, ref ushort count)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ObjectId); count += sizeof(int);
        ushort NameLen = (ushort)(this.Name != null ? Encoding.UTF8.GetByteCount(this.Name) : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), NameLen); count += sizeof(ushort);
        if (this.Name != null)
        {
            Encoding.UTF8.GetBytes(this.Name, span.Slice(count)); count += NameLen;
        }
        if (this.PosInfo != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.PosInfo.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }
        if (this.StatInfo != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.StatInfo.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

    }
}


public class PositionInfo
{
    public CreatureState State;
    public MoveDir MoveDir;
    public int PosX;
    public int PosY;
    public int PosZ;

    public void Read(ReadOnlySpan<byte> span, ref ushort count)
    {
        this.State = (CreatureState)BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.MoveDir = (MoveDir)BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.PosX = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.PosY = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.PosZ = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);

    }

    public void Write(Span<byte> span, ref ushort count)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), (int)this.State); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), (int)this.MoveDir); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.PosX); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.PosY); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.PosZ); count += sizeof(int);

    }
}


public class StatInfo
{
    public int Level;
    public int Hp;
    public int MaxHp;
    public int Attack;
    public float Speed;
    public int TotalExp;

    public void Read(ReadOnlySpan<byte> span, ref ushort count)
    {
        this.Level = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Hp = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.MaxHp = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Attack = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Speed = BitConverter.ToSingle(span.Slice(count)); count += sizeof(float);
        this.TotalExp = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);

    }

    public void Write(Span<byte> span, ref ushort count)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.Level); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.Hp); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.MaxHp); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.Attack); count += sizeof(int);
        BitConverter.TryWriteBytes(span.Slice(count), this.Speed); count += sizeof(float);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.TotalExp); count += sizeof(int);

    }
}


public class SkillInfo
{
    public int SkillId;

    public void Read(ReadOnlySpan<byte> span, ref ushort count)
    {
        this.SkillId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);

    }

    public void Write(Span<byte> span, ref ushort count)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.SkillId); count += sizeof(int);

    }
}


public class ItemInfo
{
    public int ItemDbId;
    public int TemplateId;
    public int Count;
    public int Slot;
    public bool Equipped;

    public void Read(ReadOnlySpan<byte> span, ref ushort count)
    {
        this.ItemDbId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.TemplateId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Count = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Slot = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Equipped = BitConverter.ToBoolean(span.Slice(count)); count += sizeof(bool);

    }

    public void Write(Span<byte> span, ref ushort count)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ItemDbId); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.TemplateId); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.Count); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.Slot); count += sizeof(int);
        BitConverter.TryWriteBytes(span.Slice(count), this.Equipped); count += sizeof(bool);

    }
}


        public class S_EnterGame : IPacket
        {
            public ObjectInfo Player;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                byte PlayerHasValue = span[count]; count += sizeof(byte);
        if (PlayerHasValue != 0)
        {
            this.Player = new ObjectInfo();
            this.Player.Read(span, ref count);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                if (this.Player != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.Player.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_EnterGame);
            }
        }


        public class S_LeaveGame : IPacket
        {
        

            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
        
            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
        
                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_LeaveGame);
            }
        }


        public class S_Spawn : IPacket
        {
            public List<ObjectInfo> Objects = new List<ObjectInfo>();


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                ushort ObjectsLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Objects = new List<ObjectInfo>();
        for (int i = 0; i < ObjectsLen; i++)
        {
            byte itemHasValue = span[count]; count += sizeof(byte);
            ObjectInfo item = null;
            if (itemHasValue != 0)
            {
                item = new ObjectInfo();
                item.Read(span, ref count);
            }
            this.Objects.Add(item);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                ushort ObjectsLen = (ushort)(this.Objects != null ? this.Objects.Count : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), ObjectsLen); count += sizeof(ushort);
        if (this.Objects != null)
        {
            foreach (var item in this.Objects)
            {
                if (item != null)
                {
                    span[count] = 1; count += sizeof(byte);
                    item.Write(span, ref count);
                }
                else
                {
                    span[count] = 0; count += sizeof(byte);
                }
            }
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Spawn);
            }
        }


        public class S_Despawn : IPacket
        {
            public List<int> ObjectIds = new List<int>();


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                ushort ObjectIdsLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.ObjectIds = new List<int>();
        for (int i = 0; i < ObjectIdsLen; i++)
        {
            int item = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
            this.ObjectIds.Add(item);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                ushort ObjectIdsLen = (ushort)(this.ObjectIds != null ? this.ObjectIds.Count : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), ObjectIdsLen); count += sizeof(ushort);
        if (this.ObjectIds != null)
        {
            foreach (var item in this.ObjectIds)
            {
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), item); count += sizeof(int);
            }
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Despawn);
            }
        }


        public class C_Move : IPacket
        {
            public PositionInfo PosInfo;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                byte PosInfoHasValue = span[count]; count += sizeof(byte);
        if (PosInfoHasValue != 0)
        {
            this.PosInfo = new PositionInfo();
            this.PosInfo.Read(span, ref count);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                if (this.PosInfo != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.PosInfo.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.C_Move);
            }
        }


        public class S_Move : IPacket
        {
            public int ObjectId;
    public PositionInfo PosInfo;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.ObjectId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        byte PosInfoHasValue = span[count]; count += sizeof(byte);
        if (PosInfoHasValue != 0)
        {
            this.PosInfo = new PositionInfo();
            this.PosInfo.Read(span, ref count);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ObjectId); count += sizeof(int);
        if (this.PosInfo != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.PosInfo.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Move);
            }
        }


        public class C_Skill : IPacket
        {
            public SkillInfo Info;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                byte InfoHasValue = span[count]; count += sizeof(byte);
        if (InfoHasValue != 0)
        {
            this.Info = new SkillInfo();
            this.Info.Read(span, ref count);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                if (this.Info != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.Info.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.C_Skill);
            }
        }


        public class S_Skill : IPacket
        {
            public int ObjectId;
    public SkillInfo Info;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.ObjectId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        byte InfoHasValue = span[count]; count += sizeof(byte);
        if (InfoHasValue != 0)
        {
            this.Info = new SkillInfo();
            this.Info.Read(span, ref count);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ObjectId); count += sizeof(int);
        if (this.Info != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.Info.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Skill);
            }
        }


        public class S_ChangeHp : IPacket
        {
            public int ObjectId;
    public int Hp;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.ObjectId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Hp = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ObjectId); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.Hp); count += sizeof(int);

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_ChangeHp);
            }
        }


        public class S_Die : IPacket
        {
            public int ObjectId;
    public int AttackerId;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.ObjectId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.AttackerId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ObjectId); count += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.AttackerId); count += sizeof(int);

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Die);
            }
        }


        public class S_Connected : IPacket
        {
        

            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
        
            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
        
                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Connected);
            }
        }


        public class C_Login : IPacket
        {
            public int AccountID;
    public string Token;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.AccountID = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        ushort TokenLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Token = Encoding.UTF8.GetString(span.Slice(count, TokenLen)); count += TokenLen;

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.AccountID); count += sizeof(int);
        ushort TokenLen = (ushort)(this.Token != null ? Encoding.UTF8.GetByteCount(this.Token) : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), TokenLen); count += sizeof(ushort);
        if (this.Token != null)
        {
            Encoding.UTF8.GetBytes(this.Token, span.Slice(count)); count += TokenLen;
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.C_Login);
            }
        }


        public class S_Login : IPacket
        {
            public int LoginOk;
    public List<LobbyPlayerInfo> Players = new List<LobbyPlayerInfo>();


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.LoginOk = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        ushort PlayersLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Players = new List<LobbyPlayerInfo>();
        for (int i = 0; i < PlayersLen; i++)
        {
            byte itemHasValue = span[count]; count += sizeof(byte);
            LobbyPlayerInfo item = null;
            if (itemHasValue != 0)
            {
                item = new LobbyPlayerInfo();
                item.Read(span, ref count);
            }
            this.Players.Add(item);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.LoginOk); count += sizeof(int);
        ushort PlayersLen = (ushort)(this.Players != null ? this.Players.Count : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), PlayersLen); count += sizeof(ushort);
        if (this.Players != null)
        {
            foreach (var item in this.Players)
            {
                if (item != null)
                {
                    span[count] = 1; count += sizeof(byte);
                    item.Write(span, ref count);
                }
                else
                {
                    span[count] = 0; count += sizeof(byte);
                }
            }
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Login);
            }
        }


        public class C_EnterGame : IPacket
        {
            public string Name;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                ushort NameLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Name = Encoding.UTF8.GetString(span.Slice(count, NameLen)); count += NameLen;

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                ushort NameLen = (ushort)(this.Name != null ? Encoding.UTF8.GetByteCount(this.Name) : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), NameLen); count += sizeof(ushort);
        if (this.Name != null)
        {
            Encoding.UTF8.GetBytes(this.Name, span.Slice(count)); count += NameLen;
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.C_EnterGame);
            }
        }


        public class C_CreatePlayer : IPacket
        {
            public string Name;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                ushort NameLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Name = Encoding.UTF8.GetString(span.Slice(count, NameLen)); count += NameLen;

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                ushort NameLen = (ushort)(this.Name != null ? Encoding.UTF8.GetByteCount(this.Name) : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), NameLen); count += sizeof(ushort);
        if (this.Name != null)
        {
            Encoding.UTF8.GetBytes(this.Name, span.Slice(count)); count += NameLen;
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.C_CreatePlayer);
            }
        }


        public class S_CreatePlayer : IPacket
        {
            public LobbyPlayerInfo Player;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                byte PlayerHasValue = span[count]; count += sizeof(byte);
        if (PlayerHasValue != 0)
        {
            this.Player = new LobbyPlayerInfo();
            this.Player.Read(span, ref count);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                if (this.Player != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.Player.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_CreatePlayer);
            }
        }


        public class S_ItemList : IPacket
        {
            public List<ItemInfo> Items = new List<ItemInfo>();


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                ushort ItemsLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Items = new List<ItemInfo>();
        for (int i = 0; i < ItemsLen; i++)
        {
            byte itemHasValue = span[count]; count += sizeof(byte);
            ItemInfo item = null;
            if (itemHasValue != 0)
            {
                item = new ItemInfo();
                item.Read(span, ref count);
            }
            this.Items.Add(item);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                ushort ItemsLen = (ushort)(this.Items != null ? this.Items.Count : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), ItemsLen); count += sizeof(ushort);
        if (this.Items != null)
        {
            foreach (var item in this.Items)
            {
                if (item != null)
                {
                    span[count] = 1; count += sizeof(byte);
                    item.Write(span, ref count);
                }
                else
                {
                    span[count] = 0; count += sizeof(byte);
                }
            }
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_ItemList);
            }
        }


        public class S_AddItem : IPacket
        {
            public List<ItemInfo> Items = new List<ItemInfo>();


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                ushort ItemsLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.Items = new List<ItemInfo>();
        for (int i = 0; i < ItemsLen; i++)
        {
            byte itemHasValue = span[count]; count += sizeof(byte);
            ItemInfo item = null;
            if (itemHasValue != 0)
            {
                item = new ItemInfo();
                item.Read(span, ref count);
            }
            this.Items.Add(item);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                ushort ItemsLen = (ushort)(this.Items != null ? this.Items.Count : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), ItemsLen); count += sizeof(ushort);
        if (this.Items != null)
        {
            foreach (var item in this.Items)
            {
                if (item != null)
                {
                    span[count] = 1; count += sizeof(byte);
                    item.Write(span, ref count);
                }
                else
                {
                    span[count] = 0; count += sizeof(byte);
                }
            }
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_AddItem);
            }
        }


        public class C_EquipItem : IPacket
        {
            public int ItemDbId;
    public bool Equipped;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.ItemDbId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Equipped = BitConverter.ToBoolean(span.Slice(count)); count += sizeof(bool);

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ItemDbId); count += sizeof(int);
        BitConverter.TryWriteBytes(span.Slice(count), this.Equipped); count += sizeof(bool);

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.C_EquipItem);
            }
        }


        public class S_EquipItem : IPacket
        {
            public int ItemDbId;
    public bool Equipped;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                this.ItemDbId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);
        this.Equipped = BitConverter.ToBoolean(span.Slice(count)); count += sizeof(bool);

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), this.ItemDbId); count += sizeof(int);
        BitConverter.TryWriteBytes(span.Slice(count), this.Equipped); count += sizeof(bool);

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_EquipItem);
            }
        }


        public class S_ChangeStat : IPacket
        {
            public StatInfo StatInfo;


            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
                byte StatInfoHasValue = span[count]; count += sizeof(byte);
        if (StatInfoHasValue != 0)
        {
            this.StatInfo = new StatInfo();
            this.StatInfo.Read(span, ref count);
        }

            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
                if (this.StatInfo != null)
        {
            span[count] = 1; count += sizeof(byte);
            this.StatInfo.Write(span, ref count);
        }
        else
        {
            span[count] = 0; count += sizeof(byte);
        }

                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_ChangeStat);
            }
        }


        public class S_Ping : IPacket
        {
        

            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
        
            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
        
                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.S_Ping);
            }
        }


        public class C_Pong : IPacket
        {
        

            public void Read(ReadOnlySpan<byte> span)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
        
            }

            public void Write(Span<byte> span, out ushort size)
            {
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
        
                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.C_Pong);
            }
        }

}
