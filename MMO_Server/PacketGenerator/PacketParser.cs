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
        // Google Sheets API는 trailing empty cell을 잘라서 반환한다.
        // (예: payload 없는 패킷 행 `1 \t S_LeaveGame \t \t` 는 `[1, S_LeaveGame]` 2개만 돌아옴)
        // → 행이 짧아도 무시하지 말고 빈 문자열로 패딩해서 인덱싱이 안전하도록.
        private static string[] PadCols(string raw, int minCols)
        {
            string[] cols = raw.Split('\t');
            if (cols.Length >= minCols) return cols;
            string[] padded = new string[minCols];
            for (int i = 0; i < minCols; i++)
                padded[i] = i < cols.Length ? cols[i] : "";
            return padded;
        }

        // 1. Packet 파싱
        public List<PacketDef> ParsePackets(string tsvData)
        {
            List<PacketDef> list = new List<PacketDef>();
            PacketDef current = null;
            string[] lines = tsvData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++)
            {
                // 최소 2열(id, name)만 있으면 패킷 선언 행으로 간주 가능.
                // 필드 행은 cols[2](FieldType)가 채워진 경우에만 추가.
                string[] cols = PadCols(lines[i], 4);

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
                string[] cols = PadCols(lines[i], 3);

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
                string[] cols = PadCols(lines[i], 3);

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

