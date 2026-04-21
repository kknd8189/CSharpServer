using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace PacketGenerator
{
    public class SheetDownloader
    {
        // 💡 구글 API 스코프 설정 (읽기 전용)
        static string[] Scopes = { SheetsService.Scope.SpreadsheetsReadonly };
        static string ApplicationName = "PacketGeneratorTool";

        public static string DownloadTsvWithAuth(string spreadsheetId, string sheetNameAndRange)
        {
            Console.WriteLine($"[System] 비공개 시트({sheetNameAndRange}) 인증 및 다운로드 시작...");

            GoogleCredential credential;

            // 1. 아까 다운받은 secret.json 파일로 인증!
            using (var stream = new FileStream("../../../Common/universal-rex-412207-da191721c2af.json", FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }

            // 2. 구글 시트 서비스 객체 생성
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            // 3. 데이터 요청
            SpreadsheetsResource.ValuesResource.GetRequest request = service.Spreadsheets.Values.Get(spreadsheetId, sheetNameAndRange);
            ValueRange response = request.Execute();
            IList<IList<object>> values = response.Values;

            if (values == null || values.Count == 0)
            {
                Console.WriteLine("[Error] 시트에 데이터가 없습니다.");
                return null;
            }

            // 4. 기존 파서가 읽을 수 있도록 데이터를 TSV 문자열로 변환 (기존 코드 100% 호환!)
            StringBuilder sb = new StringBuilder();
            foreach (var row in values)
            {
                // 각 행의 컬럼들을 탭(\t)으로 묶고, 끝에 엔터(\n) 추가
                sb.AppendLine(string.Join("\t", row));
            }

            Console.WriteLine("[System] 다운로드 및 TSV 변환 성공!");
            return sb.ToString();
        }
    }
}