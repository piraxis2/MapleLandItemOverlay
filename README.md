![Image](https://private-user-images.githubusercontent.com/47844565/535508600-b1fbc2f0-3b44-4c33-a5bb-c56555846a5d.gif?jwt=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJnaXRodWIuY29tIiwiYXVkIjoicmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbSIsImtleSI6ImtleTUiLCJleHAiOjE3NjgzODE0MTcsIm5iZiI6MTc2ODM4MTExNywicGF0aCI6Ii80Nzg0NDU2NS81MzU1MDg2MDAtYjFmYmMyZjAtM2I0NC00YzMzLWE1YmItYzU2NTU1ODQ2YTVkLmdpZj9YLUFtei1BbGdvcml0aG09QVdTNC1ITUFDLVNIQTI1NiZYLUFtei1DcmVkZW50aWFsPUFLSUFWQ09EWUxTQTUzUFFLNFpBJTJGMjAyNjAxMTQlMkZ1cy1lYXN0LTElMkZzMyUyRmF3czRfcmVxdWVzdCZYLUFtei1EYXRlPTIwMjYwMTE0VDA4NTgzN1omWC1BbXotRXhwaXJlcz0zMDAmWC1BbXotU2lnbmF0dXJlPWFiNTAwNzIyOGU4MDgxZDg5MDc2OTZjYzE5MmFlZDA2MDExZGE2YTVlZTNkNDA2MmNkMGQ1MDYwM2IyYTFiMmYmWC1BbXotU2lnbmVkSGVhZGVycz1ob3N0In0.N-ZEg6rvU5-iWR8NMudvsdwHp8d-AIKj88sZJY_S6Qo)


클릭해서 작동 확인

# MapleOverlay
메랜 득템후 매번 검색하기 귀찮아서 만든 프로그램

OCR로 아이템 이름을 인식해서 클래식 버전 기준 스탯과 상점가를 보여줌 (GMS v62 버전)

KMS는 100 이하의 버전을 검색할 수 없어서 KMS 389 버전에서 이름으로 검색해서 ID를 가져와 GMS 62 버전에서 ID로 다시 검색해서 스텟을 가져옴

## 사용법

1. **게임 실행**
   - 아이템 위에 마우스를 올려 툴팁을 띄움.

2. **캡처 (` 키)**
   - **`** 키를 누르면 화면이 정지됨.

3. **드래그**
   - 아이템 이름 부분을 마우스로 드래그함.
   - 마우스를 떼면 검색 결과가 뜸.

4. **수동 검색 (\ 키)**
   - **\** 키를 누르거나 메뉴(F12)에서 '수동 검색'을 클릭해 직접 검색 가능함.

## 기능

- **오버레이 메뉴**: 화면 우측 상단의 `☰` 버튼을 눌러 메뉴를 열 수 있음.
- **최소화**: 메뉴 패널의 `─` 버튼을 누르면 작업 표시줄로 최소화됨.
- **설정**: 메뉴의 '설정' 버튼을 눌러 단축키를 마음대로 바꿀 수 있음.
- **상점가 확인**: 아이템 검색 시 상점 판매가가 함께 표시됨 (GMS v62 기준).

## 단축키 (기본값)

| 키 | 기능 |
| :--- | :--- |
| **`** | 캡처 모드 시작 |
| **\** | 수동 검색 열기 |
| **F12** | 메뉴 열기 / 닫기 |
| **ESC** | 창 닫기 / 취소 |

*단축키는 메뉴의 '설정'에서 변경 가능함.*

## 주의사항

- `tessdata`, `x64` 폴더는 실행 파일과 같은 곳에 있어야 함.

## 기술 스택

- .NET 8 (WPF)
- Tesseract OCR 5
- MapleStory.io API

---

## ⚠️ 면책 조항 (Disclaimer)

이 프로그램은 게임 메모리를 건드리거나 패킷을 조작하지 않고, 단순히 화면에 보이는 글자를 인식(OCR)해서 정보를 보여주는 오버레이 유틸리티임.

하지만 게임사 운영 정책에 따라 외부 프로그램 사용은 제재 대상이 될 수 있음. **이 프로그램 사용으로 인해 발생하는 계정 정지나 불이익에 대해서는 개발자가 책임지지 않음.** 쓸 사람은 이 점 유의해서 사용하길 바람.
