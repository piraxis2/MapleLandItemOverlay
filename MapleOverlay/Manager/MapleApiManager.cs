using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

public class MapleApiManager
{
    private static readonly HttpClient client = new HttpClient();
    
    // 검색용: KMS 최신 버전 (한글 이름 검색 및 ID 확인용)
    private const string SearchBaseUrl = "https://maplestory.io/api/KMS/384";
    
    // 상세 정보용: GMS v62 (빅뱅 전 클래식 메이플 데이터 - 스탯 및 상점가용)
    private const string DetailBaseUrl = "https://maplestory.io/api/GMS/62";

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

    // 2. ID로 상세 정보 조회 (GMS v62 데이터만 사용)
    public async Task<JObject> GetItemDetailAsync(int itemId)
    {
        try
        {
            // GMS v62 데이터만 가져옵니다. (정확한 클래식 스탯 및 상점가)
            // 한글 이름과 설명은 SearchItemAsync 결과에서 이미 확보하여 UI에 전달되므로 여기서 KMS를 다시 조회할 필요가 없습니다.
            string url = $"{DetailBaseUrl}/item/{itemId}";
            var response = await client.GetStringAsync(url);
            return JObject.Parse(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Detail Error (GMS): {ex.Message}");
            return null;
        }
    }
}