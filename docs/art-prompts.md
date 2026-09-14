# 캐릭터 아트 제작 기록

이 앱의 캐릭터 그림은 사용자가 첨부한 연한 분홍색 도트 마이멜로디를 스타일 참고로 삼아, Codex의 **내장 imagegen** 도구로 새로 제작했습니다. 첨부 그림 자체나 서명을 앱 자산으로 복사하지 않았습니다. CLI/API 대체 경로는 사용하지 않았습니다.

## 최종 파일 규격

- 위치: `src/MyMelody.App/Assets/Characters/{theme}-{stage}.png`
- 9개 테마 × 3단계 = 27개 PNG, 각 PNG에 2개의 애니메이션 프레임을 가로로 배치합니다.
- 왼쪽은 기본 눈 뜬 모습, 오른쪽은 눈 감는 모습입니다. 2개의 정사각형 셀을 같은 폭으로 잘라 표시합니다.
- 실제 알파 채널이 있는 투명 PNG 원본을 그대로 포함합니다. 앱에서는 WPF `CroppedBitmap`으로 프레임을 선택하고 작은 위치 이동과 함께 재생합니다.
- 캐릭터는 기본 96 DIP, 관리창에서 약 200 DIP로 표시합니다. 별도의 이미지 확대 편집 없이 앱에서 표시 크기만 조정합니다.
- `assets-manifest.json`은 파일별 프레임 수와 규격을, `generation-manifest.json`은 정확한 개별 프롬프트와 생성 원본 식별자 및 SHA-256을 기록합니다.

## 테마와 성장 표현

| ID | 테마 | 처음 만남 | 친해짐 | 단짝 소품 |
|---|---|---|---|---|
| ribbon | 리본 | 작은 귀 리본 | 더 밝은 표정과 목 리본 | 하트와 큰 리본 |
| piano | 피아노 | 음표 장식 | 미소와 목 리본 | 작은 피아노 |
| strawberry | 딸기 | 딸기 장식 | 미소와 목 리본 | 딸기 바구니 |
| pajamas | 잠옷 | 달 장식의 수면 모자 | 밝은 표정과 잠옷 장식 | 하트 베개 |
| garden | 정원 | 데이지 장식 | 미소와 목 리본 | 작은 물뿌리개 |
| baking | 베이킹 | 컵케이크 장식 | 미소와 목 리본 | 앞치마와 믹싱 볼 |
| reading | 독서 | 책갈피 장식 | 미소와 목 리본 | 펼친 동화책 |
| rain | 비 오는 날 | 빗방울 장식 | 미소와 목 리본 | 우비·장화·접은 우산 |
| starry | 별밤 | 별 장식 | 미소와 목 리본 | 달 목걸이·별 지팡이 |

성장 단계마다 표정과 소품이 추가됩니다. 같은 단계의 두 프레임은 눈 표정만 바뀌는 짧은 깜빡임으로 사용합니다. 성장 완료 연출과 연주 음표 반응은 앱에서 더합니다.

## 사용한 공통 프롬프트

아래 공통문 뒤에 테마와 단계별 세부 지시를 붙였습니다. 파일별로 완전히 조합된 정확한 프롬프트는 `generation-manifest.json`의 `prompt` 필드를 확인합니다. 첫 검증용 `ribbon-2.png`도 동일한 2프레임 규격으로 만들었으며 그 파일의 원문을 별도로 기록했습니다.

```text
Use case: stylized-concept. Asset type: ONE transparent horizontal 2-frame PNG sprite sheet for a personal Windows desktop pet displayed at96x96DIP. Output exactly TWO equal square cells side by side, 2:1 overall aspect ratio. Draw My Melody, the cute Sanrio white rabbit with blush-pink hood and two very long ears, left upright, right gently bent, oversized round head and tiny chubby body, cream-white face/fur, tiny yellow oval nose. Front-facing FULL BODY centered in each cell, identical exact size/position/silhouette/proportions/pose/accessories in both cells, with generous transparent safety margin around ears and feet. LEFT CELL: both eyes open tiny dark dots. RIGHT CELL: same pose and mouth, only eyes gently CLOSED as two tiny dark curves for blinking. Pink/dusty-rose chunky hard square pixel-art outlines, cream-white and blush-pink flat colors, small kawaii proportions, very soft pastel cute appearance, pixel edges, limited palette, no blur. Background MUST be true transparent ALPHA, not gray, not white, not black, not a checkerboard illustration! Do not paint any surrounding background at all. No divider, no text, labels, signature, watermark, frame, shadow, glow, or other characters.
```

## 검수와 재생

각 결과를 눈으로 확인한 뒤, PNG 픽셀 포맷·투명 배경 알파·2:1 시트 비율·개별 셀 규격·파일 수를 검사합니다. 코드로 캐릭터 그림을 대체하거나 배경을 지우지 않습니다.

초기 파일 참조 편집은 투명 배경 대신 불투명 체크무늬를 만들었으므로 폐기했습니다. 최종 납품 파일은 모두 새 이미지 생성으로 만든 투명 시트입니다. 폐기한 후보는 앱 자산과 배포 패키지에 포함하지 않습니다.
