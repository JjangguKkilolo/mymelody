# 마이멜로디 연습 친구

MIDI 건반을 연습하면 함께 자라는 작은 Windows 데스크톱 친구입니다.

![홈 화면](docs/screenshots/home.png)

위 화면의 시간과 수집 상태는 기능 검증을 위해 만든 시험 기록입니다.

## 시작하기

[최신 릴리스](https://github.com/JjangguKkilolo/mymelody/releases/latest)에서 `MyMelodyPractice-win-Setup.exe` 또는 `MyMelodyPractice-win-Portable.zip`을 받습니다. 포터블은 ZIP을 폴더 전체로 풀고 **마이멜로디 연습 친구.exe**를 실행하세요.

1. **첫 친구 만나기**를 눌러 마이멜로디를 뽑습니다.
2. **설정 → 연습 입력**에서 사용하는 MIDI 건반을 선택합니다.
3. 건반을 치면 자동 기록됩니다. Cakewalk가 꺼져 있어도 동작합니다.
4. 입력이 멈추면 30초의 여유 시간 후 자동으로 쉽니다. 악보 읽기 등은 **수동 연습 시작**으로 기록할 수 있습니다.

Windows x64용이며 .NET 런타임은 앱에 포함됩니다. Cakewalk와 동시에 입력을 받을 수 있는지는 MIDI 장치 드라이버의 다중 앱 연결 지원에 따라 달라집니다.

## 함께 성장하기

| 누적 연습 | 변화 |
|---|---|
| 0시간 | 1단계 |
| 12시간 | 2단계 · 새로운 표정 |
| 24시간 | 3단계 · 테마 소품 |
| 36시간 | 성장 완료 · 다음 친구 뽑기 |

리본, 피아노, 딸기, 잠옷, 정원, 베이킹, 독서, 비 오는 날, 별밤의 9종입니다. 아직 없는 친구 중 무작위로 뽑으며 중복은 없습니다. 한 번에 한 친구만 자라고 완성한 친구는 언제든 바탕화면에 표시할 수 있습니다. 표시 대상과 육성 대상은 별개입니다. 새 친구를 뽑기 전의 연습은 기록만 쌓입니다.

![9종의 세 단계 모습](docs/screenshots/characters.png)

## 기록과 사용 편의

- 오늘·이번 주·전체 시간, 월별 달력과 세션 기록
- 캐릭터 크기 48–384 DIP, 드래그 이동, 항상 위에 표시, 숨기기
- 트레이 아이콘에서 열기·숨기기·일시정지·종료
- Windows 로그인 시 자동 실행 (설정에서 선택, 기본 꺼짐)
- 창의 닫기 버튼은 트레이로 숨깁니다. 완전히 끝내려면 트레이의 **종료**를 사용하세요.
- 5초마다 자동 저장, 일일 백업 최근 7개, 백업 내보내기·복원
- PC 잠금·절전·MIDI 연결 해제 시 자동 기록 중단

개인 데이터는 `%AppData%\MyMelodyPractice`에만 저장합니다. 프로그램 업데이트와 독립된 위치이며, GitHub로 연습 기록을 업로드하지 않습니다.

## 업데이트

실행 시와 6시간마다 공개 GitHub Releases에서 새 정식 버전을 확인합니다. **업데이트하고 다시 시작**을 누르기 전에는 다운로드·설치하지 않습니다. 설치 전 기록을 저장하고 백업합니다. 설정에서 자동 확인을 끄거나 직접 확인할 수 있습니다.

## 개발

.NET 10 SDK가 필요합니다. 저장소의 `global.json`에 SDK 버전이 고정되어 있습니다.

```powershell
dotnet build MyMelody.slnx -c Release
dotnet test MyMelody.slnx -c Release
dotnet run --project src/MyMelody.App
```

저장소 안에 SDK를 따로 준비했다면 `dotnet` 대신 `.\.tooling\dotnet\dotnet.exe`를 사용합니다.

```powershell
./scripts/package.ps1 -Version 1.0.0
```

`vMAJOR.MINOR.PATCH` 태그를 push하면 GitHub Actions가 테스트·빌드·패키징 후 모든 파일을 릴리스 초안에 업로드하고 게시합니다. 설치형·포터블·업데이트 패키지와 체크섬 파일이 포함됩니다.

검증 범위와 재현 방법은 [검증 문서](docs/verification.md), [친구에게 전달할 테스트 안내](docs/friend-testing.md), 그림 생성 프롬프트는 [아트 문서](docs/art-prompts.md)를 참고하세요.

개인용 비공식 팬 프로젝트입니다. My Melody 캐릭터는 Sanrio의 캐릭터이며 이 앱은 Sanrio의 공식 제품이 아닙니다.
