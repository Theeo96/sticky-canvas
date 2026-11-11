# 개발 안내

Windows x64 / .NET Framework 4.8. 소스는 `apps/desktop/`입니다.

| 파일 | 역할 |
|---|---|
| Models.cs | JSON·검증·실행 취소 |
| Store.cs / Sqlite.cs | 저장·첨부·백업, OS SQLite C API |
| NoteWindow.cs / ElementView.cs | 편집·표시 |
| PresentationAndInk.cs | 내용만 보기·도구 표시·필기 |
| NoteChrome.cs | 자동 대비·도구 테마·제목 표시줄 이동 |
| Placement.cs | 서식 펼침 표시·정렬·격자 |
| Program.cs | 트레이·수명·자동 실행 |

`scripts/build.ps1 -Test`로 빌드와 인공 데이터 검사를 실행합니다. 실제 사용자 데이터를 검사에 쓰지 마세요.

UI 도구가 도구 창을 찾지 못하면 `--profile <별도 경로> --ui-test`로 시험 창을 노출할 수 있습니다. 기본 작업표시줄 동작과 구분하세요. 시험 창은 Ctrl+Shift+Q로 정상 종료할 수 있습니다.

`scripts/measure-resources.ps1 -Count 5 -IncludeHidden`는 짧은 유휴 측정입니다. 큰 사진·장시간·펜 연속 입력 검증을 대신하지 않습니다. 주기적 강제 GC와 작업 집합 강제 비우기를 사용하지 않습니다.

`scripts/package.ps1`는 버전 폴더와 `release/current`를 갱신합니다. 앱을 먼저 정상 종료해야 하며 실사용 프로세스를 강제 종료하지 마세요.

공개 전 `scripts/check-public-files.ps1`와 staged diff를 확인하세요. 개인 운영 문서는 `docs/public` 밖에 보관합니다. 별도의 이용 허락 라이선스는 아직 지정하지 않았습니다.


SelectionTransforms.cs는 전체 선택 프레임·비율 변형·간격·잠금을, PixelDrawing.cs는 투명 PNG 필기·부분 지우기·브러시 슬라이더를 담당합니다. 지우기 중 픽셀 버퍼를 재사용하고 제스처 종료 시 첨부를 저장합니다. 저장 전에도 진행 중 제스처를 마무리합니다.


ImageEditing.cs는 인라인 불투명도 미리보기와 원본 기준 그래픽 자르기의 편집 세션을 관리합니다. 세션 미리보기와 저장 문서를 분리하고 자동 저장은 세션 종료까지 보류합니다. 수동 저장/닫기에서는 먼저 세션을 확정합니다. 불투명도 슬라이더는 기존 Image.Opacity만 갱신합니다.


PenColor.cs는 펜 색상 견본과 메모 내부 색 추출을 담당합니다. 클릭 시 VisualBrush로 선택 장식을 숨긴 메모 영역의 지정 픽셀을 1×1 RenderTargetBitmap에 렌더링합니다. 확대율과 무관한 문서 좌표를 사용합니다. 화면 전체 캡처나 반복 타이머는 사용하지 않습니다.
