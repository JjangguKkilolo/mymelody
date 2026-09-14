# 캐릭터 아트 제작 기록

이 앱의 캐릭터 그림은 사용자가 첨부한 도트 마이멜로디를 스타일 참고로 삼아, Codex의 **내장 imagegen** 도구로 새로 제작했습니다. 1.1.0에서는 추가로 제공한 의상·계란 도트를 참고해 기존 9종을 리메이크하고 양·계란 2종을 추가했습니다. 첨부 그림 자체나 서명을 앱 자산으로 복사하지 않았습니다. CLI/API 대체 경로는 사용하지 않았습니다.

## 최종 파일 규격

- 위치: `src/MyMelody.App/Assets/Characters/{id}.png`
- 11종 × 3단계 × 2표정 = 66개 프레임을 11개 PNG 시트에 담았습니다. 한 시트는 2열×3행, 전체 비율은 2:3입니다.
- 왼쪽 열은 눈 뜬 모습, 오른쪽 열은 눈 감는 모습입니다. 위에서부터 1·2·3단계이며 정사각형 셀을 같은 크기로 잘라 표시합니다.
- 실제 알파 채널이 있는 투명 PNG 원본을 그대로 포함합니다. 앱에서는 WPF `CroppedBitmap`으로 프레임을 선택하고 작은 위치 이동과 함께 재생합니다.
- 캐릭터는 기본 96 DIP, 관리창에서 약 200 DIP로 표시합니다. 별도의 이미지 확대 편집 없이 앱에서 표시 크기만 조정합니다.
- `assets-manifest.json`의 schemaVersion 2는 그림 배치 규격입니다. **개인 저장 데이터베이스의 schemaVersion은 1로 유지**됩니다. `generation-manifest.json`은 정확한 개별 프롬프트와 생성 원본 식별자 및 SHA-256을 기록합니다.

## 테마와 성장 표현

| ID | 테마 | 처음 만남 | 친해짐 | 단짝 소품 |
|---|---|---|---|---|
| ribbon | 리본 | 흰 물방울무늬 토끼 모자·분홍 치마 | 큰 리본·하트 | 겹리본·하트 선물·앉은 자세 |
| piano | 피아노 | 베레모·연주복·나비넥타이 | 건반무늬 옷자락·악보 | 작은 피아노를 연주하는 자세 |
| strawberry | 딸기 | 씨앗무늬 딸기 모자·크림 | 크림 위 딸기·손에 든 딸기 | 딸기 컵케이크·앉은 자세 |
| pajamas | 잠옷 | 라벤더 수면 모자·줄무늬 잠옷 | 취침 리본·미소 | 하트 베개 |
| garden | 정원 | 민트 체크무늬 모자·멜빵옷 | 큰 데이지·새싹 화분 | 물뿌리개·앉은 자세 |
| baking | 베이킹 | 요리사 모자·복숭아빛 앞치마·주걱 | 분홍 오븐 장갑 | 믹싱 볼 |
| reading | 독서 | 베레모·민트 카디건·안경·책 | 책갈피·미소 | 앉아서 펼친 책 읽기 |
| rain | 비 오는 날 | 구름무늬 하늘색 우비·노란 장화 | 무지개 장식·접은 우산 | 펼친 분홍 우산·기울인 자세 |
| starry | 별밤 | 별빛 마법사 모자·망토 | 별 장식·미소 | 별 지팡이 |
| sheep | 양 | 복슬복슬한 양 모자·양옷 | 방울 목걸이·미소 | 품에 안은 실뭉치 |
| egg | 계란 | 얼굴이 보이는 동그란 분홍 알 | 금이 간 흰 껍데기·노란 리본 | 알에서 깨어난 모습·작은 꽃 |

성장 단계마다 표정과 소품이 추가됩니다. 같은 단계의 두 프레임은 눈 표정만 바뀌는 짧은 깜빡임으로 사용합니다. 성장 완료 연출과 연주 음표 반응은 앱에서 더합니다.

## 사용한 공통 프롬프트

각 테마마다 의상·귀 모양·자세와 세 단계의 차이를 지정해 한 번씩 생성했습니다. 아래는 공통 제작 방향의 요약이며, 실제 도구에 전달한 파일별 정확한 프롬프트는 `generation-manifest.json`의 `prompt` 필드에 보존했습니다.

```text
Create one transparent PNG sprite sheet with exactly two columns and three rows of equal square cells, overall 2:3 aspect ratio. Each row is a growth stage; left column open eyes, right column the same pose and accessories with closed eyes for blinking. Draw full-body My Melody with an oversized cream-white face, tiny yellow nose and small round body. Use chunky hard pixel-art outlines, a limited soft pastel palette and readable compact shapes. Give each theme a distinct costume and silhouette from stage one, then add expressions and props. Center every sprite with transparent safety margins. Require real alpha transparency, no painted background, text, divider or watermark.
```

## 검수와 재생

각 결과를 눈으로 확인한 뒤, PNG 픽셀 포맷·투명 배경 알파·2:3 시트 비율·개별 셀 규격·파일 수를 검사합니다. WPF 앱의 실제 렌더러로 기본 96 DIP에서 33개 모습을 모은 미리보기를 확인합니다. 코드로 캐릭터 그림을 대체하거나 배경을 지우지 않습니다.

생성 원본의 투명 픽셀에는 배경색 RGB가 남아 있을 수 있지만 알파 값은 0이며, WPF에서는 투명하게 표시됩니다. 원본 알파를 유지한 채 프로젝트에 복사했습니다. 이전 버전의 27개 시트는 Git 이력에 남고, 새 배포에는 현재 11개 시트만 포함합니다. 캐릭터 ID는 이전 9종을 그대로 유지하며 개인 기록을 변환하거나 초기화하지 않습니다.
