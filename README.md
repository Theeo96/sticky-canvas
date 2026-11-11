# Sticky Canvas · 캔버스 메모

<img src="assets/app-icon-128.png" alt="캔버스 메모 아이콘" width="80" />

바탕화면의 포스트잇처럼 쓰는 **Windows 네이티브 캔버스 메모 앱**입니다. 글상자·도형·이미지·손그림을 한 메모 안에서 자유롭게 배치합니다.

현재 버전: **v0.4.0** · Windows x64 · C# / WPF / .NET Framework 4.8 · 로컬 저장

## 기능

- 메모별 독립 창, 크기 조절, 항상 위, 트레이 상주. 작업표시줄/Alt+Tab에 표시하지 않습니다.
- 평소에는 내용과 작은 좌상단 버튼만 표시합니다. 좌상단 버튼으로만 **상단 도구**를 열고 닫습니다.
- 글상자·글꼴·크기·색·굵게·기울임·밑줄·문단 정렬.
- 도형과 이미지, 이동·크기·앞뒤 순서·복제.
- 펜·색·굵기·선 지우개. 글상자·도형·이미지·서식을 선택하면 그리기 모드를 자동 해제합니다.
- 종이 기준 정렬과 이동 시 8칸 격자 맞춤.
- 자동 저장, 검색, 휴지통·복구, 실행 취소, ZIP 백업/사본 가져오기.
- Windows 로그인 시 자동 실행 옵션.

## 실행

GitHub Releases의 Windows ZIP을 압축 해제하고 `StickyCanvas.exe`를 실행합니다. `.config` 파일을 같은 폴더에 둡니다. .NET Framework 4.8이 필요합니다.

트레이 더블클릭은 목록, 우클릭은 새 메모·백업·자동 실행·완전 종료입니다. 창의 `×`는 창만 닫습니다. 완전 종료는 **트레이 → 캔버스 메모 종료**입니다.

상단 도구는 내용 위에 겹쳐 표시합니다. 열고 닫아도 내용의 화면 위치·표시 배율은 바뀌지 않습니다. 도구 배경은 메모와 같은 색이고, UI 글자는 배경 대비에 따라 검정/흰색으로 바뀝니다. 메모 본문의 지정 색상은 유지합니다. 창 이동은 상단 제목 표시줄 드래그, 제목 편집은 더블클릭입니다. 글 편집은 더블클릭/Enter, 그리기 종료는 선택/Esc입니다. `서식 ▾`는 펼치기, `서식 ▴`는 접기입니다.

## 데이터와 개인정보

기본 데이터는 `%LOCALAPPDATA%\StickyCanvas`에 저장합니다.

| 경로 | 내용 |
|---|---|
| `notes.sqlite3` | SQLite DB. 문서 본문은 구조화된 JSON, 창 설정·첨부 정보도 보관 |
| `assets/` | 이미지 원본 사본 |
| `backups/` | 일별 DB 복구본 |

**이 저장소에는 실제 메모·사진·DB·백업·실행 로그를 포함하지 않습니다.** 테스트는 인공 데이터만 사용합니다. 기본 실행은 로컬 동작이며 클라우드/AI 연결과 앱 자체 암호화는 아직 없습니다.

[데이터 형식](docs/public/DATA.md) · [공개 범위](docs/public/PRIVACY.md)

## 빌드

Windows PowerShell에서 실행하며 PowerShell 7을 권장합니다.

```powershell
pwsh -File scripts/build.ps1 -Test
pwsh -File scripts/package.ps1
```

외부 NuGet 패키지는 사용하지 않습니다. 설치된 Visual Studio C# 컴파일러를 우선 사용하고 없으면 Windows .NET Framework 컴파일러를 사용합니다. 결과는 `bin/`, 패키지는 `release/`에 생성합니다. 검사는 별도 인공 데이터 프로필을 사용합니다.

게시 전 점검: `pwsh -File scripts/check-public-files.ps1`.

## 범위와 제한

- Windows x64 로컬 앱입니다. 웹·모바일·동기화·AI 연동은 후속 계획입니다.
- 필기 문서는 스키마 2입니다. v0.1.0에서는 읽을 수 없습니다.
- PNG/JPEG 기본 지원. WebP는 Windows 코덱에 따라 달라집니다.
- 실제 IME 조합·복수 배율·재로그인·장기 상주는 환경별 추가 검증이 필요합니다.
- 비공식 바탕화면 부모 창 부착은 사용하지 않습니다. Win+D/가상 데스크톱 표시 동작은 미검증입니다.
- 시험판 EXE는 코드 서명되지 않았습니다.

[개발 계획](docs/public/ROADMAP.md) · [개발 안내](docs/public/DEVELOPMENT.md) · [변경 기록](docs/public/CHANGELOG.md)
