using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PacketGenerator
{
    class Program
    {
        static string _spreadsheetId = "1XotKHBhAAndcumdiXWSZ4jYn9DmRj7pqzDF7bbB3dCo";

        static async Task Main(string[] args)
        {
            Console.WriteLine("[System] 패킷 제너레이터 실행...");

            // 1. 다운로드
            string enumTsv = SheetDownloader.DownloadTsvWithAuth(_spreadsheetId, "Enums!A:C");
            string structTsv = SheetDownloader.DownloadTsvWithAuth(_spreadsheetId, "Structs!A:C");
            string packetTsv = SheetDownloader.DownloadTsvWithAuth(_spreadsheetId, "Packets!A:D"); ;

            // 2. 파싱
            PacketParser parser = new PacketParser();
            var enumList = parser.ParseEnums(enumTsv);
            var structList = parser.ParseStructs(structTsv);
            var packetList = parser.ParsePackets(packetTsv);

            Console.WriteLine($"[System] 파싱 완료 - Enum: {enumList.Count}, Struct: {structList.Count}, Packet: {packetList.Count}");

            // 3. 코드 생성 (모두 합쳐서 하나의 Protocol.cs로!)
            PacketGenerator generator = new PacketGenerator();
            string protocolCode = generator.GenerateAll(enumList, structList, packetList);
            string managerCode = generator.GenerateManager(packetList);

            // 4. 저장
            File.WriteAllText("Protocol.cs", protocolCode);
            File.WriteAllText("PacketManager.cs", managerCode);

            Console.WriteLine("[System] 성공적으로 C# 파일이 생성되었습니다! (Zero-Allocation 완벽 적용)");
        }
    }
}