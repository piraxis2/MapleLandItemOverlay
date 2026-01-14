using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

public class MapleApiManager
{
    private static readonly HttpClient client = new HttpClient();
    
    // 검색용: KMS 최신 버전 (한글 이름 검색을 위해 필요)
    private const string SearchBaseUrl = "https://maplestory.io/api/KMS/384";
    
    // 상세 정보용: GMS 구버전 (클래식 스탯 정보를 위해 필요)
    // GMS는 영어 기반이지만 ID 체계는 공유하므로 ID로 조회 가능
    private const string DetailBaseUrl = "https://maplestory.io/api/GMS/100";

    // 1. 이름으로 아이템 검색 (KMS에서 ID 찾기)
    public async Task<JArray> SearchItemAsync(string itemName)
    {
        try
        {
            string encodedName = Uri.EscapeDataString(itemName);
            string url = $"{SearchBaseUrl}/item?searchFor={encodedName}";
            
            var response = await client.GetStringAsync(url);
            return JArray.Parse(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Search Error: {ex.Message}");
            return null;
        }
    }

    // 2. ID로 상세 정보 조회 (GMS v100에서 옵션 가져오기)
    public async Task<JObject> GetItemDetailAsync(int itemId)
    {
        try
        {
            string url = $"{DetailBaseUrl}/item/{itemId}";
            var response = await client.GetStringAsync(url);
            return JObject.Parse(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Detail Error (GMS): {ex.Message}");
            
            // GMS에 데이터가 없으면 KMS 데이터라도 가져오도록 폴백(Fallback) 처리
            try
            {
                string fallbackUrl = $"{SearchBaseUrl}/item/{itemId}";
                var fallbackResponse = await client.GetStringAsync(fallbackUrl);
                return JObject.Parse(fallbackResponse);
            }
            catch
            {
                return null;
            }
        }
    }
}