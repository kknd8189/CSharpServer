using System;
using System.Collections.Generic;

namespace PacketGenerator
{
    public class FieldDef
    {
        public string FieldType { get; set; } // 예: "int", "string", "List<ItemInfo>"
        public string FieldName { get; set; } // 예: "playerId", "name", "items"
    }
    public class PacketDef
    {
        public int PacketId { get; set; }      // 예: 2
        public string PacketName { get; set; } // 예: "C_Move"

        // 이 패킷이 가지고 있는 변수들의 목록
        public List<FieldDef> Fields { get; set; } = new List<FieldDef>();
    }
    public class StructDef
    {
        public string StructName { get; set; } // 예: "ItemInfo"
        public List<FieldDef> Fields { get; set; } = new List<FieldDef>();
    }
    public class EnumMemberDef
    {
        public string Name { get; set; } // 예: "Idle", "Running", "Jumping"
        public int Value { get; set; }   // 예: 0, 1, 2
    }

    public class EnumDef
    {
        public string EnumName { get; set; } // 예: "PlayerState"
        public List<EnumMemberDef> Members { get; set; } = new List<EnumMemberDef>();
    }


    public class PacketParser
    {
        // 1. Packet 파싱
        public List<PacketDef> ParsePackets(string tsvData)
        {
            List<PacketDef> list = new List<PacketDef>();
            PacketDef current = null;
            string[] lines = tsvData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split('\t');
                if (cols.Length < 4) continue;

                string idStr = cols[0].Trim();
                if (!string.IsNullOrEmpty(idStr))
                {
                    int id = int.Parse(idStr);
                    if (current == null || current.PacketId != id)
                    {
                        current = new PacketDef { PacketId = id, PacketName = cols[1].Trim() };
                        list.Add(current);
                    }
                }

                if (current != null && !string.IsNullOrEmpty(cols[2].Trim()))
                {
                    current.Fields.Add(new FieldDef { FieldType = cols[2].Trim(), FieldName = cols[3].Trim() });
                }
            }
            return list;
        }

        // 2. Struct 파싱
        public List<StructDef> ParseStructs(string tsvData)
        {
            List<StructDef> list = new List<StructDef>();
            StructDef current = null;
            string[] lines = tsvData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split('\t');
                if (cols.Length < 3) continue;

                string structName = cols[0].Trim();
                if (!string.IsNullOrEmpty(structName))
                {
                    if (current == null || current.StructName != structName)
                    {
                        current = new StructDef { StructName = structName };
                        list.Add(current);
                    }
                }

                if (current != null && !string.IsNullOrEmpty(cols[1].Trim()))
                {
                    current.Fields.Add(new FieldDef { FieldType = cols[1].Trim(), FieldName = cols[2].Trim() });
                }
            }
            return list;
        }

        // 3. Enum 파싱
        public List<EnumDef> ParseEnums(string tsvData)
        {
            List<EnumDef> list = new List<EnumDef>();
            EnumDef current = null;
            string[] lines = tsvData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split('\t');
                if (cols.Length < 3) continue;

                string enumName = cols[0].Trim();
                if (!string.IsNullOrEmpty(enumName))
                {
                    if (current == null || current.EnumName != enumName)
                    {
                        current = new EnumDef { EnumName = enumName };
                        list.Add(current);
                    }
                }

                if (current != null && !string.IsNullOrEmpty(cols[1].Trim()))
                {
                    current.Members.Add(new EnumMemberDef { Name = cols[1].Trim(), Value = int.Parse(cols[2].Trim()) });
                }
            }
            return list;
        }
    }
}

