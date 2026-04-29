using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace PacketGenerator
{
    public class PacketGenerator
    {
        // 스프레드시트에서 파싱된 enum 이름 집합
        // List<CreatureState> 같은 경우 innerType이 enum인지 구조체인지 판별하는 데 사용
        private HashSet<string> _enumNames = new HashSet<string>();

        // 💡 모든 코드(Enum, Struct, Packet)를 한 방에 구워내는 마스터 함수
        public string GenerateAll(List<EnumDef> enums, List<StructDef> structs, List<PacketDef> packets)
        {
            // MsgId는 Packets 시트에서 자동 생성 (PacketName = PacketId)
            // → 스프레드시트 Enums 시트에 MsgId를 수동으로 유지할 필요 없음.
            // 혹시 사용자가 Enums 시트에 MsgId를 또 넣어놨다면 중복 방지로 걸러냄.
            var msgIdEnum = new EnumDef
            {
                EnumName = "MsgId",
                Members = packets.Select(p => new EnumMemberDef
                {
                    Name = p.PacketName,
                    Value = p.PacketId
                }).ToList()
            };
            var allEnums = new List<EnumDef> { msgIdEnum };
            allEnums.AddRange(enums.Where(e => e.EnumName != "MsgId"));

            // 하드코딩된 enum 리스트 대신 실제 파싱된 enum 이름들로 판별
            _enumNames = new HashSet<string>(allEnums.Select(e => e.EnumName));

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using System.Buffers.Binary;");
            sb.AppendLine("using ServerCore;");
            sb.AppendLine();
            sb.AppendLine("namespace Protocol");
            sb.AppendLine("{");

            // 1. Enum 굽기 (자동 생성된 MsgId가 가장 앞)
            foreach (var e in allEnums)
            {
                StringBuilder memberCode = new StringBuilder();
                foreach (var member in e.Members)
                {
                    // Protobuf C# 관례대로 SCREAMING_SNAKE_CASE → PascalCase 변환 +
                    // enum 이름과 겹치는 prefix는 스트립. MsgId 멤버(S_EnterGame 등)는
                    // 이미 PascalCase라 변환 대상 아님.
                    string name = TransformEnumMemberName(e.EnumName, member.Name);
                    memberCode.AppendLine(string.Format(PacketFormat.enumMemberFormat, name, member.Value));
                }

                sb.AppendLine(string.Format(PacketFormat.enumFormat, e.EnumName, memberCode.ToString()));
            }

            // 2. Struct 굽기
            foreach (var s in structs)
            {
                StringBuilder memberCode = new StringBuilder();
                StringBuilder readCode = new StringBuilder();
                StringBuilder writeCode = new StringBuilder();

                foreach (FieldDef field in s.Fields)
                {
                    memberCode.AppendLine(GenerateMemberLine(field));
                    readCode.AppendLine(GenerateReadLogic(field));
                    writeCode.AppendLine(GenerateWriteLogic(field));
                }
                sb.AppendLine(string.Format(PacketFormat.structFormat, s.StructName, memberCode.ToString(), readCode.ToString(), writeCode.ToString()));
            }

            // 3. Packet 굽기
            foreach (var p in packets)
            {
                StringBuilder memberCode = new StringBuilder();
                StringBuilder readCode = new StringBuilder();
                StringBuilder writeCode = new StringBuilder();

                foreach (FieldDef field in p.Fields)
                {
                    memberCode.AppendLine(GenerateMemberLine(field));
                    readCode.AppendLine(GenerateReadLogic(field));
                    writeCode.AppendLine(GenerateWriteLogic(field));
                }
                sb.AppendLine(string.Format(PacketFormat.packetClassFormat, p.PacketName, memberCode.ToString(), readCode.ToString(), writeCode.ToString()));
            }

            sb.AppendLine("}"); // namespace 닫기
            return sb.ToString();
        }

        // 매니저 코드 굽기. prefixFilter로 등록할 패킷 방향 제한.
        //   - 서버: "C_" → 서버가 수신하는 클라 → 서버 방향 패킷만 등록
        //   - 클라: "S_" → 서버 → 클라 방향 패킷만 등록
        // 배열 크기는 전체 maxId+1로 잡아서 반대 방향 ID 접근해도 ArgOutOfRange 안 터지게.
        public string GenerateManager(List<PacketDef> packets, string prefixFilter)
        {
            StringBuilder registerCode = new StringBuilder();
            int maxId = 0;

            foreach (PacketDef p in packets)
            {
                if (p.PacketId > maxId) maxId = p.PacketId;

                if (!p.PacketName.StartsWith(prefixFilter))
                    continue;

                registerCode.AppendLine(string.Format(PacketManagerFormat.managerRegisterFormat, p.PacketName));
            }

            return string.Format(PacketManagerFormat.managerFormat, registerCode.ToString(), maxId + 1);
        }

        // protobuf C# 관례와 동일: SCREAMING_SNAKE 멤버는 PascalCase로, 그리고
        // enum 이름과 같은 prefix가 붙어있으면 제거.
        // 예: ItemType.ITEM_TYPE_WEAPON → Weapon, PlayerServerState.SERVER_STATE_LOGIN → ServerStateLogin,
        //     CreatureState.IDLE → Idle, MsgId.S_EnterGame → S_EnterGame(변환 안 됨).
        private static string TransformEnumMemberName(string enumName, string rawValue)
        {
            // SCREAMING_SNAKE 패턴이 아니면 이미 처리된 이름으로 간주하고 그대로 유지
            if (!System.Text.RegularExpressions.Regex.IsMatch(rawValue, "^[A-Z0-9_]+$"))
                return rawValue;

            string prefix = ToScreamingSnake(enumName) + "_";
            string core = rawValue.StartsWith(prefix) ? rawValue.Substring(prefix.Length) : rawValue;
            if (core.Length == 0) core = rawValue; // prefix 스트립으로 빈 문자열 되는 극단 방지

            var sb = new StringBuilder();
            foreach (var part in core.Split(new[] { '_' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append(char.ToUpper(part[0]));
                if (part.Length > 1) sb.Append(part.Substring(1).ToLower());
            }
            return sb.Length == 0 ? rawValue : sb.ToString();
        }

        // "ItemType" → "ITEM_TYPE", "PlayerServerState" → "PLAYER_SERVER_STATE"
        private static string ToScreamingSnake(string pascalCase)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascalCase.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascalCase[i]))
                    sb.Append('_');
                sb.Append(char.ToUpper(pascalCase[i]));
            }
            return sb.ToString();
        }

        // 필드 선언 라인. List<T>는 즉시 new로 초기화해서 송신측에서 .Add() NRE 방지.
        private string GenerateMemberLine(FieldDef field)
        {
            if (field.FieldType.StartsWith("List<"))
                return $"    public {field.FieldType} {field.FieldName} = new {field.FieldType}();";
            return string.Format(PacketFormat.memberFormat, field.FieldType, field.FieldName);
        }

        // 필드 1개의 Read 코드 생성
        private string GenerateReadLogic(FieldDef field)
        {
            string type = field.FieldType;
            string name = field.FieldName;

            if (type.StartsWith("List<"))
            {
                string innerType = type.Substring(5, type.Length - 6).Trim();
                string readElement = GenerateReadListElement(innerType);
                return $@"        ushort {name}Len = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.{name} = new List<{innerType}>();
        for (int i = 0; i < {name}Len; i++)
        {{
{readElement}
            this.{name}.Add(item);
        }}";
            }

            if (type == "string")
            {
                return $@"        ushort {name}Len = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.{name} = Encoding.UTF8.GetString(span.Slice(count, {name}Len)); count += {name}Len;";
            }

            if (type == "bool")
                return $@"        this.{name} = BitConverter.ToBoolean(span.Slice(count)); count += sizeof(bool);";

            if (type == "float")
                return $@"        this.{name} = BitConverter.ToSingle(span.Slice(count)); count += sizeof(float);";

            if (_enumNames.Contains(type))
                return $@"        this.{name} = ({type})BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);";

            string method = GetBinaryPrimitivesReadMethod(type);
            if (!string.IsNullOrEmpty(method))
                return $@"        this.{name} = BinaryPrimitives.{method}(span.Slice(count)); count += sizeof({type});";

            // 그 외는 struct/class. nullable 보존을 위해 1바이트 presence flag 선행.
            return $@"        byte {name}HasValue = span[count]; count += sizeof(byte);
        if ({name}HasValue != 0)
        {{
            this.{name} = new {type}();
            this.{name}.Read(span, ref count);
        }}";
        }

        private string GenerateWriteLogic(FieldDef field)
        {
            string type = field.FieldType;
            string name = field.FieldName;

            if (type.StartsWith("List<"))
            {
                string innerType = type.Substring(5, type.Length - 6).Trim();
                string writeElement = GenerateWriteListElement(innerType);
                return $@"        ushort {name}Len = (ushort)(this.{name} != null ? this.{name}.Count : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), {name}Len); count += sizeof(ushort);
        if (this.{name} != null)
        {{
            foreach (var item in this.{name})
            {{
{writeElement}
            }}
        }}";
            }

            if (type == "string")
            {
                return $@"        ushort {name}Len = (ushort)(this.{name} != null ? Encoding.UTF8.GetByteCount(this.{name}) : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), {name}Len); count += sizeof(ushort);
        if (this.{name} != null)
        {{
            Encoding.UTF8.GetBytes(this.{name}, span.Slice(count)); count += {name}Len;
        }}";
            }

            if (type == "bool")
                return $@"        BitConverter.TryWriteBytes(span.Slice(count), this.{name}); count += sizeof(bool);";

            if (type == "float")
                return $@"        BitConverter.TryWriteBytes(span.Slice(count), this.{name}); count += sizeof(float);";

            if (_enumNames.Contains(type))
                return $@"        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), (int)this.{name}); count += sizeof(int);";

            string method = GetBinaryPrimitivesWriteMethod(type);
            if (!string.IsNullOrEmpty(method))
                return $@"        BinaryPrimitives.{method}(span.Slice(count), this.{name}); count += sizeof({type});";

            // 그 외는 struct/class. presence flag 1바이트 + 본체.
            return $@"        if (this.{name} != null)
        {{
            span[count] = 1; count += sizeof(byte);
            this.{name}.Write(span, ref count);
        }}
        else
        {{
            span[count] = 0; count += sizeof(byte);
        }}";
        }

        // List<T> 루프 내부에서 한 원소를 읽는 코드.
        // 반드시 로컬변수 "item"을 선언하고 거기에 값을 채워넣는 형태로 생성한다.
        private string GenerateReadListElement(string type)
        {
            if (type == "string")
                return $@"            ushort itemLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
            string item = Encoding.UTF8.GetString(span.Slice(count, itemLen)); count += itemLen;";

            if (type == "bool")
                return $@"            bool item = BitConverter.ToBoolean(span.Slice(count)); count += sizeof(bool);";

            if (type == "float")
                return $@"            float item = BitConverter.ToSingle(span.Slice(count)); count += sizeof(float);";

            if (_enumNames.Contains(type))
                return $@"            {type} item = ({type})BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);";

            string method = GetBinaryPrimitivesReadMethod(type);
            if (!string.IsNullOrEmpty(method))
                return $@"            {type} item = BinaryPrimitives.{method}(span.Slice(count)); count += sizeof({type});";

            // 그 외는 struct/class. List 원소 단위로도 presence flag로 null 보존.
            return $@"            byte itemHasValue = span[count]; count += sizeof(byte);
            {type} item = null;
            if (itemHasValue != 0)
            {{
                item = new {type}();
                item.Read(span, ref count);
            }}";
        }

        // List<T> 루프 내부에서 한 원소를 쓰는 코드.
        // 바깥 foreach가 "item" 변수를 제공함.
        private string GenerateWriteListElement(string type)
        {
            if (type == "string")
                return $@"                ushort itemLen = (ushort)(item != null ? Encoding.UTF8.GetByteCount(item) : 0);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), itemLen); count += sizeof(ushort);
                if (item != null)
                {{
                    Encoding.UTF8.GetBytes(item, span.Slice(count)); count += itemLen;
                }}";

            if (type == "bool")
                return $@"                BitConverter.TryWriteBytes(span.Slice(count), item); count += sizeof(bool);";

            if (type == "float")
                return $@"                BitConverter.TryWriteBytes(span.Slice(count), item); count += sizeof(float);";

            if (_enumNames.Contains(type))
                return $@"                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), (int)item); count += sizeof(int);";

            string method = GetBinaryPrimitivesWriteMethod(type);
            if (!string.IsNullOrEmpty(method))
                return $@"                BinaryPrimitives.{method}(span.Slice(count), item); count += sizeof({type});";

            // 그 외는 struct/class. presence flag 1바이트 + 본체.
            return $@"                if (item != null)
                {{
                    span[count] = 1; count += sizeof(byte);
                    item.Write(span, ref count);
                }}
                else
                {{
                    span[count] = 0; count += sizeof(byte);
                }}";
        }

        private string GetBinaryPrimitivesReadMethod(string type)
        {
            switch (type)
            {
                case "int": return "ReadInt32LittleEndian";
                case "long": return "ReadInt64LittleEndian";
                case "short": return "ReadInt16LittleEndian";
                case "ushort": return "ReadUInt16LittleEndian";
                default: return "";
            }
        }

        private string GetBinaryPrimitivesWriteMethod(string type)
        {
            switch (type)
            {
                case "int": return "WriteInt32LittleEndian";
                case "long": return "WriteInt64LittleEndian";
                case "short": return "WriteInt16LittleEndian";
                case "ushort": return "WriteUInt16LittleEndian";
                default: return "";
            }
        }
    }
}
