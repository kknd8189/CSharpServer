using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PacketGenerator
{
    class Program
    {
        static string _spreadsheetId = "1XotKHBhAAndcumdiXWSZ4jYn9DmRj7pqzDF7bbB3dCo";

        // 출력 타겟 정의.
        // cwd는 PacketGenerator/bin/ 이므로 모든 경로는 거기 기준 상대경로.
        // ManagerFile: 각 타겟의 매니저 파일명. PrefixFilter: 수신 대상 패킷 prefix.
        private class OutputTarget
        {
            public string Dir;
            public string ManagerFile;
            public string PrefixFilter;  // "C_" = 서버, "S_" = 클라
        }

        static readonly OutputTarget[] Targets = new[]
        {
            new OutputTarget
            {
                Dir = "../../Server/Packet",
                ManagerFile = "ServerPacketManager.cs",
                PrefixFilter = "C_",
            },
            new OutputTarget
            {
                Dir = "../../../Client/Assets/Script/Packet",
                ManagerFile = "ClientPacketManager.cs",
                PrefixFilter = "S_",
            },
        };

        static async Task Main(string[] args)
        {
            Console.WriteLine("[System] 패킷 제너레이터 실행...");

            // 1. 다운로드
            string enumTsv = SheetDownloader.DownloadTsvWithAuth(_spreadsheetId, "Enums!A:C");
            string structTsv = SheetDownloader.DownloadTsvWithAuth(_spreadsheetId, "Structs!A:C");
            string packetTsv = SheetDownloader.DownloadTsvWithAuth(_spreadsheetId, "Packets!A:D");

            // 2. 파싱
            PacketParser parser = new PacketParser();
            var enumList = parser.ParseEnums(enumTsv);
            var structList = parser.ParseStructs(structTsv);
            var packetList = parser.ParsePackets(packetTsv);

            Console.WriteLine($"[System] 파싱 완료 - Enum: {enumList.Count}, Struct: {structList.Count}, Packet: {packetList.Count}");

            // 3. Protocol.cs 는 양쪽 공용. 매니저는 타겟별로 수신 방향에 맞춰 따로 구움.
            PacketGenerator generator = new PacketGenerator();
            string protocolCode = generator.GenerateAll(enumList, structList, packetList);

            // 4. 타겟별 저장
            foreach (var t in Targets)
            {
                if (!Directory.Exists(t.Dir))
                {
                    Console.WriteLine($"[Warn] 출력 경로 없음: {Path.GetFullPath(t.Dir)} - 스킵");
                    continue;
                }

                string managerCode = generator.GenerateManager(packetList, t.PrefixFilter);

                string protocolPath = Path.Combine(t.Dir, "Protocol.cs");
                string managerPath = Path.Combine(t.Dir, t.ManagerFile);

                File.WriteAllText(protocolPath, protocolCode);
                File.WriteAllText(managerPath, managerCode);

                Console.WriteLine($"[OK] {Path.GetFullPath(protocolPath)}");
                Console.WriteLine($"[OK] {Path.GetFullPath(managerPath)} (filter: {t.PrefixFilter}*)");
            }

            Console.WriteLine("[System] 성공적으로 C# 파일이 생성되었습니다! (Zero-Allocation 완벽 적용)");
        }
    }
}