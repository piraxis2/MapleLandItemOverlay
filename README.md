![](https://private-user-images.githubusercontent.com/47844565/535508600-b1fbc2f0-3b44-4c33-a5bb-c56555846a5d.gif?jwt=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJnaXRodWIuY29tIiwiYXVkIjoicmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbSIsImtleSI6ImtleTUiLCJleHAiOjE3NjgzODE0MTcsIm5iZiI6MTc2ODM4MTExNywicGF0aCI6Ii80Nzg0NDU2NS81MzU1MDg2MDAtYjFmYmMyZjAtM2I0NC00YzMzLWE1YmItYzU2NTU1ODQ2YTVkLmdpZj9YLUFtei1BbGdvcml0aG09QVdTNC1ITUFDLVNIQTI1NiZYLUFtei1DcmVkZW50aWFsPUFLSUFWQ09EWUxTQTUzUFFLNFpBJTJGMjAyNjAxMTQlMkZ1cy1lYXN0LTElMkZzMyUyRmF3czRfcmVxdWVzdCZYLUFtei1EYXRlPTIwMjYwMTE0VDA4NTgzN1omWC1BbXotRXhwaXJlcz0zMDAmWC1BbXotU2lnbmF0dXJlPWFiNTAwNzIyOGU4MDgxZDg5MDc2OTZjYzE5MmFlZDA2MDExZGE2YTVlZTNkNDA2MmNkMGQ1MDYwM2IyYTFiMmYmWC1BbXotU2lnbmVkSGVhZGVycz1ob3N0In0.N-ZEg6rvU5-iWR8NMudvsdwHp8d-AIKj88sZJY_S6Qo)

# MapleOverlay
메랜 득템후 매번 검색하기 귀찮아서 만든 프로그램

OCR로 아이템 이름을 인식해서 클래식 버전 기준 스탯을 보여줌 (100 버전)

KMS는 100 이하의 버전을 검색할 수 없어서 KMS 389 버전에서 이름으로 검색해서 ID를 가져와 GMS 100 버전에서 ID로 다시 검색해서 스텟을 가져옴
## 사용법

1. **게임 실행**
   - 아이템 위에 마우스를 올려 툴팁을 띄움.

2. **캡처 (` 키)**
   - **`** 키를 누르면 화면이 정지됨.

3. **드래그**
   - 아이템 이름 부분을 마우스로 드래그함.
   - 마우스를 떼면 검색 결과가 뜸.

## 단축키

| 키 | 기능 |
| :--- | :--- |
| **`** | 캡처 모드 시작 |
| **\\** | 수동 검색창 열기 |
| **ESC** | 창 닫기 / 취소 |
| **F10** | 프로그램 종료 |

## 설정 변경

`config.json` 파일을 열어서 단축키 코드를 수정할 수 있음.

키 코드는 [Virtual-Key Codes](https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes)를 참고

```json
{
  "KeyCapture": 192,      // `
  "KeyManualSearch": 220, // \
  "KeyExit": 121,         // F10
  "KeyClosePanel": 27     // ESC
}
```

## 주의사항

- `tessdata`, `x64` 폴더는 실행 파일과 같은 곳에 있어야 함.

## 기술 스택

- .NET 8 (WPF)
- Tesseract OCR 5
- MapleStory.io API
