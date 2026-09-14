# 실제 업데이트 확인용 작은 실행 프로그램

앱의 `UpdateService`, Velopack 백엔드, SQLite 연습 저장 코드를 그대로 링크한다. `run.ps1`은 버전 1.0.0과 1.0.1을 self-contained Windows x64로 패키징하고, 독립적인 테스트 설치 폴더와 데이터 폴더에서 실제 업데이트 후 재시작을 확인한다.

```powershell
./tests/MyMelody.UpdateSmoke/run.ps1
```

시험 결과는 `artifacts/UpdateSmoke/<run-id>`에 남는다. 공개 GitHub 릴리스나 실제 앱의 `%AppData%`는 사용하지 않는다. 테스트용 앱 ID는 `MyMelodyUpdateSmoke`이다.

확인 항목: 확인만으로는 다운로드되지 않음, 다운로드 및 패키지 검증, 기록 저장과 백업 후 실제 업데이트/재시작, 캐릭터 2단계·13시간 기록·음표 수·화면 설정 보존, 백업 복원, 새 입력 없이 연습 미적립, 설치형과 포터블 동작, 번들 런타임 사용.

이 검사는 기존 컴퓨터에서 외부 .NET 탐색을 비활성화한 self-contained 실행을 확인한다. SDK가 전혀 설치되지 않은 별도 Windows VM 검사와 실제 MIDI 장치·Cakewalk 동시 입력 검사를 대체하지 않는다.
