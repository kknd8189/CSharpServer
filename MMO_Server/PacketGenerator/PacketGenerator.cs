using System.Collections.Generic;
using System.Text;
namespace PacketGenerator
{
    public class PacketGenerator
    {
        // 💡 모든 코드(Enum, Struct, Packet)를 한 방에 구워내는 마스터 함수
        public string GenerateAll(List<EnumDef> enums, List<StructDef> structs, List<PacketDef> packets)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using System.Buffers.Binary;");
            sb.AppendLine("using ServerCore;");
            sb.AppendLine();
            sb.AppendLine("namespace Protocol");
            sb.AppendLine("{");

            // 1. Enum 굽기
            foreach (var e in enums)
            {
                StringBuilder memberCode = new StringBuilder();
                foreach (var member in e.Members)
                    memberCode.AppendLine(string.Format(PacketFormat.enumMemberFormat, member.Name, member.Value));

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
                    memberCode.AppendLine(string.Format(PacketFormat.memberFormat, field.FieldType, field.FieldName));
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
                    memberCode.AppendLine(string.Format(PacketFormat.memberFormat, field.FieldType, field.FieldName));
                    readCode.AppendLine(GenerateReadLogic(field));
                    writeCode.AppendLine(GenerateWriteLogic(field));
                }
                sb.AppendLine(string.Format(PacketFormat.packetClassFormat, p.PacketName, memberCode.ToString(), readCode.ToString(), writeCode.ToString()));
            }

            sb.AppendLine("}"); // namespace 닫기
            return sb.ToString();
        }

        // 💡 매니저 코드 굽기 (기존과 동일)
        public string GenerateManager(List<PacketDef> packets)
        {
            StringBuilder registerCode = new StringBuilder();
            int maxId = 0;

            foreach (PacketDef p in packets)
            {
                registerCode.AppendLine(string.Format(PacketManagerFormat.managerRegisterFormat, p.PacketName));
                if (p.PacketId > maxId) maxId = p.PacketId;
            }

            return string.Format(PacketManagerFormat.managerFormat, registerCode.ToString(), maxId + 1);
        }
        // 기본 타입 Read 헬퍼 (string, List 등은 나중에 추가 예정)
        private string GenerateReadLogic(FieldDef field)
        {
            string type = field.FieldType;
            string name = field.FieldName;

            if (type.StartsWith("List<"))
            {
                string innerType = type.Replace("List<", "").Replace(">", "");
                return $@"        ushort {name}Len = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(count)); count += sizeof(ushort);
        this.{name} = new List<{innerType}>();
        for (int i = 0; i < {name}Len; i++)
        {{
            {innerType} item = new {innerType}();
            item.Read(span, ref count);
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

            // Enum 캐스팅 처리 (CreatureState, MoveDir 등)
            if (type == "CreatureState" || type == "MoveDir" || type == "GameObjectType" || type == "SkillType" || type == "ItemType")
            {
                return $@"        this.{name} = ({type})BinaryPrimitives.ReadInt32LittleEndian(span.Slice(count)); count += sizeof(int);";
            }

            string bitConverterMethod = GetBinaryPrimitivesReadMethod(type);
            if (!string.IsNullOrEmpty(bitConverterMethod))
            {
                return $@"        this.{name} = BinaryPrimitives.{bitConverterMethod}(span.Slice(count)); count += sizeof({type});";
            }

            // 나머지 구조체 (StatInfo, PositionInfo 등)
            return $@"        this.{name} = new {type}();
        this.{name}.Read(span, ref count);";
        }

        private string GenerateWriteLogic(FieldDef field)
        {
            string type = field.FieldType;
            string name = field.FieldName;

            if (type.StartsWith("List<"))
            {
                return $@"        ushort {name}Len = (ushort)(this.{name} != null ? this.{name}.Count : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(count), {name}Len); count += sizeof(ushort);
        if (this.{name} != null)
        {{
            foreach (var item in this.{name})
                item.Write(span, ref count);
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

            // Enum 캐스팅 처리
            if (type == "CreatureState" || type == "MoveDir" || type == "GameObjectType" || type == "SkillType" || type == "ItemType")
            {
                return $@"        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(count), (int)this.{name}); count += sizeof(int);";
            }

            string bitConverterMethod = GetBinaryPrimitivesWriteMethod(type);
            if (!string.IsNullOrEmpty(bitConverterMethod))
            {
                return $@"        BinaryPrimitives.{bitConverterMethod}(span.Slice(count), this.{name}); count += sizeof({type});";
            }

            return $@"        if (this.{name} != null)
            this.{name}.Write(span, ref count);";
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


