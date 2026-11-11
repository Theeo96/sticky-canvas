$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskRelease = Join-Path $taskRoot 'release\0.4.0'
New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
foreach ($taskName in @('StickyCanvas.exe','StickyCanvas.exe.config')) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $taskRoot 'bin') $taskName) -Destination (Join-Path $taskRelease $taskName) -Force
}
$taskGuide = @'
캔버스 메모 v0.4.0 — Windows x64

평소에는 내용과 좌상단 작은 ☰ 버튼만 보입니다.
좌상단 ☰ 버튼을 누를 때마다 상단 도구를 표시/숨깁니다.
도구는 내용 위에 겹쳐 표시됩니다. 내용 위치와 배율은 변하지 않습니다.
도구 배경은 메모 배경색과 같으며 글자색은 대비에 따라 검정/흰색으로 자동 조절됩니다.
글상자·도형·이미지·서식을 누르면 펜/지우개 모드가 자동 종료됩니다. 서식 ▾/▴는 펼침 상태를 나타냅니다.
배치 ▾에서 종이 기준 정렬과 이동 시 8칸 격자 맞춤을 사용할 수 있습니다.
펜/지우개 모드에서도 도구를 숨길 수 있습니다. 선택 또는 Esc로 그리기를 종료합니다.
펜 색·굵기, 선 전체 지우기, 필기 이동·크기 조절·실행 취소·저장을 지원합니다.
필기를 넣은 메모는 문서 버전 2가 되어 v0.1.0에서 열 수 없습니다.

실행: StickyCanvas.exe 더블클릭. StickyCanvas.exe.config를 같은 폴더에 둡니다.
메모 창은 작업표시줄과 Alt+Tab에 표시하지 않습니다.
트레이의 노란 메모 아이콘 더블클릭: 메모 목록.
트레이 우클릭: 새 메모 / 보이기·숨기기 / 백업 / 데이터 폴더 / 완전 종료.
모든 창을 닫아도 계속 실행됩니다. 종료는 트레이 우클릭 → 캔버스 메모 종료.

창 이동: 상단 제목 표시줄 드래그. 제목 더블클릭: 편집. Enter/Esc/다른 곳 클릭: 편집 마침. 창 크기: 테두리/모서리.
글상자 편집: 더블클릭 또는 선택 후 Enter.
요소 이동: 클릭 후 위쪽 파란 손잡이 드래그. 크기: 선택 모서리.
Aa 서식: 글꼴·크기·굵게·기울임·밑줄·색·정렬.
도형: 사각형·둥근 사각형·타원·선·화살표.
이미지: PNG/JPEG 파일 선택·드롭·붙여넣기. WebP는 시스템 코덱에 따라 다릅니다.
요소 우클릭: 위치·크기·색·여백·순서·복제·삭제.
⋯ 메뉴: 배경색·종이 크기·가려진 요소 선택·휴지통.

Ctrl+N 새 메모 / Ctrl+Shift+M 목록 / Ctrl+S 저장
Ctrl+Z 취소 / Ctrl+Y 다시 실행 / 선택 요소 Ctrl+D 복제
Delete 선택 요소 삭제 / Esc 편집 종료 / 방향키 미세 이동

저장은 자동입니다. 오른쪽 아래 ‘로컬 저장됨’을 확인하세요.
닫기는 창 숨기기이며, 삭제는 휴지통 이동입니다. 목록의 휴지통에서 복구할 수 있습니다.
백업은 ZIP으로 내보내며 가져오기는 기존 데이터를 덮어쓰지 않는 사본 추가입니다.
데이터: %LOCALAPPDATA%\StickyCanvas (트레이 메뉴에서 열기)
자동 일별 DB 복구본은 backups 폴더에 최대 7개 보관하고 이미지는 assets에 보관합니다.

현재는 로컬 앱입니다. 웹·모바일·동기화·AI 연동은 후속 단계입니다.
배경화면 레이어에 붙이는 셸 확장은 사용하지 않았으며 Win+D/가상 데스크톱은 미검증입니다.
로그인 시 자동 실행은 트레이 또는 메모 ⋯ 메뉴에서 켜고 끕니다.
업데이트할 때 앱을 트레이에서 완전히 종료하세요. release/current를 고정 실행 위치로 사용합니다.
개인 시험판으로 코드 서명은 없습니다. 보안 설정을 해제할 필요는 없습니다.

상세 기획·사용 안내·검증 기록은 원래 프로젝트 폴더의 docs/를 참고하세요.
문제가 생기면 저장 실패 표시 여부와 했던 조작을 알려주세요. 원본 데이터 폴더는 먼저 보존하세요.
'@
[System.IO.File]::WriteAllText((Join-Path $taskRelease '사용안내.txt'), $taskGuide, (New-Object System.Text.UTF8Encoding($true)))
$taskCurrent = Join-Path $taskRoot 'release\current'
New-Item -ItemType Directory -Path $taskCurrent -Force | Out-Null
foreach ($taskName in @('StickyCanvas.exe','StickyCanvas.exe.config','사용안내.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskRelease $taskName) -Destination (Join-Path $taskCurrent $taskName) -Force
}
$taskZip = Join-Path $taskRoot 'release\StickyCanvas-Windows-x64-0.4.0.zip'
Compress-Archive -LiteralPath @((Join-Path $taskRelease 'StickyCanvas.exe'),(Join-Path $taskRelease 'StickyCanvas.exe.config'),(Join-Path $taskRelease '사용안내.txt')) -DestinationPath $taskZip -Force
Get-FileHash -LiteralPath (Join-Path $taskRelease 'StickyCanvas.exe') -Algorithm SHA256 | Select-Object Path,Hash
Get-Item -LiteralPath $taskZip | Select-Object FullName,Length
