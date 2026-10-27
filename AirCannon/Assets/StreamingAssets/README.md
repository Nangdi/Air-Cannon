# StreamingAssets

이 폴더의 JSON(`aircannon.json`, `port.json`, `tcp.json`, `Setting.json`)은 **기본값 원본**입니다.

- 실제로 읽고 쓰는 파일은 `Application.persistentDataPath` 쪽에 있습니다.
  (Windows: `%USERPROFILE%\AppData\LocalLow\<회사명>\<제품명>\`)
- 앱을 처음 실행할 때 persistent 쪽에 파일이 없으면 이 폴더의 원본을 복사해 옵니다. 이미 있으면 덮어쓰지 않으므로
  빌드를 새로 덮어써도 현장 설정이 유지됩니다.
- 현장에서 값을 바꿀 때는 ESC 설정창 / 디버그 패널을 쓰거나 **persistent 쪽 파일**을 고칩니다 (파일을 고친 경우 앱 재시작 필요).
  기본값부터 다시 시작하려면 persistent 쪽 파일을 지우고 재시작합니다.
- 파일이 깨져 있으면 기본값으로 동작하고 로그만 남깁니다.

단축키 목록은 실행 중 **F1** 도움말에서 볼 수 있습니다.

## aircannon.json — 공기대포 리플레이 설정

| 항목 | 기본값 | 설명 |
|---|---|---|
| `cameraName` | `""` | 웹캠 이름(일부만 적어도 됨). 비우면 첫 번째 웹캠. 연결된 웹캠 목록은 시작 로그에 `[AirCannon] 웹캠 [n] 이름` 으로 출력됨 |
| `cameraWidth` / `cameraHeight` / `cameraFps` | 1920 / 1080 / 30 | 웹캠에 요청할 해상도와 fps. 웹캠이 지원하지 않으면 가까운 값으로 동작 |
| `mirrorHorizontal` | false | 좌우 반전 |
| `triggerMessages` | `["FIRE","SHOOT"]` | 이 문자열이 한 줄로 들어오면 발사로 처리 (대소문자, 앞뒤 공백 무시). 줄 끝은 `\r` 또는 `\n` |
| `offsetSeconds` | 0.3 | 신호 수신 시각 기준 몇 초 뒤부터 잘라낼지. 음수면 신호 이전 장면부터 |
| `durationSeconds` | 1.5 | 잘라낼 길이(초) |
| `autoAlign` | true | 자동 맞춤. 신호 후 화면 움직임이 처음 커지는 순간을 찾아 그 시각 기준으로 잘라냄. 못 찾으면 `offsetSeconds` 사용 |
| `motionLeadSeconds` | -0.3 | 움직임 시작 기준 몇 초 뒤부터 잘라낼지. 음수면 움직임 이전 장면부터 |
| `motionThreshold` | 3.0 | 움직임으로 볼 점수(평균 밝기 차이 x 100). 보정 화면 그래프를 보고 정함 |
| `motionSearchSeconds` | 2.0 | 신호 후 몇 초 안에서 움직임을 찾을지. 못 찾으면 이 시간이 지난 뒤 고정 offset으로 재생 |
| `motionArea` | 전체 | 움직임을 볼 영역 `x, y, width, height` (0~1, 왼쪽 위 기준). 대포 앞만 지정하면 관람객 움직임에 덜 흔들림 |
| `repeatCount` | 3 | 반복 재생 횟수. 0이면 다음 발사 신호까지 계속 반복 |
| `playbackSpeed` | 0.5 | 재생 배속. 0.5 = 슬로모션 0.5배속, 1 = 원래 속도 |
| `idleVideoFolder` | `"Idle"` | 대기 화면에서 무한 반복할 영상 폴더 (StreamingAssets 기준). 파일 이름은 상관없음. 자세한 내용은 `Idle/README.md` |
| `idleTimeoutSeconds` | 30 | 리플레이 화면에서 마지막 입력(키, 마우스, 터치, 발사 신호) 후 이 시간(초)이 지나면 리플레이를 닫고 대기 화면으로 복귀. 0이면 사용 안 함 |
| `saveClip` | false | 클립을 JPG 연속 이미지로 저장할지 여부 |
| `saveFolder` | `""` | 저장 폴더. 비우면 `persistentDataPath/Clips` |
| `jpgQuality` | 90 | 저장 JPG 품질 (1~100) |

- 화면 흐름: **대기 화면**(대기 영상 무한 반복) → 발사 신호 → 녹화된 카메라 장면을 `repeatCount`회 반복 재생 → 대기 화면.
  리플레이 중에는 대기 영상을 멈춰 두었다가 끝나면 이어서 재생합니다.
- 파일이 없으면 기본값으로 새로 만들고, 내용이 깨져 있으면 기본값으로 동작하며 로그만 남깁니다.
- 캡처 중(신호 후 offset + duration 동안)에 들어온 발사 신호는 무시됩니다.
- 테스트: Space 키를 누르면 발사 신호와 똑같이 동작합니다.

### 설정창 (ESC)

카메라 이름, 발사 문자열, 저장 폴더, 대기 영상 폴더를 뺀 항목(무입력 대기 복귀 시간 포함)은 ESC 설정창에서 -/+ 버튼과 체크로 바꿀 수 있습니다.
바꾸는 즉시 적용되고 persistent 쪽 aircannon.json에 저장됩니다(재시작 불필요). 카메라 해상도·fps를 바꾸면 웹캠을 바로 다시 엽니다. 보정 화면에서 저장한 값도 설정창을 다시 열면 보입니다.

### 타이밍 보정 화면 (F2)

1. F2로 엽니다. 이 동안은 해상도를 절반으로 낮추고 신호 전 1초 ~ 후 5초를 녹화할 수 있게 버퍼를 늘립니다.
2. 대기 중에는 그래프에 실시간 움직임 점수가 나옵니다. 가만히 있을 때 점수보다 조금 높게 감도(`-` / `=`)를 맞춥니다.
3. 발사(Space 또는 실제 신호)하면 6초를 녹화한 뒤 멈춥니다.
4. ←/→(꾹 누르면 계속 이동, Shift는 10프레임)나 슬라이더로 넘겨 보며 I로 시작점, O로 끝점을 정하고 P로 실제 배속 미리보기를 합니다.
5. Enter로 저장하면 `offsetSeconds`, `durationSeconds`, `motionLeadSeconds`, `motionThreshold`, `autoAlign`이 persistent 쪽 aircannon.json에 저장되고 바로 반영됩니다.
6. R로 다시 찍어 여러 발을 비교합니다. 최근 5발의 움직임 시작 시각과 편차가 표시됩니다. 편차가 크면 자동 맞춤(A)을 켜 둡니다.

## port.json — RS232 포트

발사 신호를 받을 포트의 `com`, `baudLate` 를 맞추고 `enabled` 를 `true` 로 바꿉니다. 켜 둔 포트 중 어느 포트로 들어와도 발사 신호로 받습니다(포트 번호는 구분하지 않음).

포트는 ESC로 함께 열리는 RS232 디버그 패널에서도 바꿀 수 있습니다.

- 줄마다 지금 PC의 COM 포트 목록이 드롭다운으로 나옵니다. 고르면 persistent 쪽 port.json에 바로 저장하고 그 포트를 다시 엽니다.
- "사용 안 함"을 고르면 `enabled = false` 로 저장됩니다. 저장된 포트가 지금 PC에 없으면 "COMx (연결 안 됨)"으로 표시됩니다.
- **포트 재연결** 버튼은 COM 목록을 새로 읽고 모든 포트를 다시 엽니다.
- 수동 송신은 열린 모든 포트로 보냅니다.
