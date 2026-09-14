# 앱 아이콘 제작 기록

내장 imagegen으로 앱의 도트 캐릭터에 어울리는 마이멜로디 얼굴 아이콘을 새로 제작했습니다.

- 원본: `src/MyMelody.App/Assets/Brand/app-icon.png`
- Windows 아이콘: `src/MyMelody.App/Assets/Brand/app.ico`
- 포함 크기: 16, 20, 24, 32, 40, 48, 64, 128, 256 픽셀
- 생성 원본 식별자: `exec-75a6c34c-16b7-4852-88aa-f1779c03bee0.png`

그림과 투명 배경은 imagegen 결과를 그대로 사용합니다. `scripts/build-icon.ps1`은 Windows에 필요한 크기와 ICO 형식으로 변환하며 원본 PNG는 보존합니다. 실행 파일, 관리창·작업 표시줄, 트레이, 설치 프로그램과 포터블 실행기에 같은 아이콘을 적용합니다.

## 실제 생성 프롬프트

```text
Use case: logo-brand. Asset type: one Windows application icon for a personal My Melody piano-practice companion, used at 16, 24, 32, 48, 256 pixels. Create a brand-new single square transparent PNG icon. Subject: My Melody's HEAD ONLY, the Sanrio white rabbit wearing a light blush-pink hood, compact upright ear on one side and gently bent ear on the other, small pink ribbon, warm cream-white face, two simple dark cocoa eyes, tiny yellow oval nose, tiny friendly smile, subtle pink cheeks. Style: charming chunky pixel-art with a strong dark dusty-rose square-step outline, flat limited pastel palette, very simple clear features. Composition: one centered large head filling 88% of the square with balanced transparent safety margin, face occupies most of the lower half, compact ears in upper half; make face and outline readable as a tiny Windows notification tray icon. Match a soft pink pixel-pet app, no detailed body, hands, scenery, notes, lettering, badges, multiple variants or sprite sheet. Background must be actual transparent ALPHA, never white, black or a painted checkerboard. No glow, shadow, gradients, signature, watermark, border tile, letters or text. One finished icon only.
```

## 재현과 확인

```powershell
./scripts/build-icon.ps1
```

앱의 `--ui-smoke` 검사는 ICO의 9개 크기, WPF 창 아이콘, 트레이 디코딩, 실행 파일에 포함된 아이콘 리소스를 확인합니다. 개인 기록·캐릭터·성장 데이터는 이 변경의 대상이 아닙니다.
