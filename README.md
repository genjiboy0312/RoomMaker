<h2 align="center">🏠 RoomMaker (Procedural Floor & Wall Generator)</h2>

<p align="center">
  <b>평면도 이미지를 기반으로 벽(Walls)과 바닥(Floors)을 자동 생성하는 Unity 스크립트</b><br>
  OpenCV를 활용해 벽선을 감지하고, 내부 영역을 구분하여 <b>방 단위 바닥 메쉬</b>를 생성합니다.
</p>

<hr>

<h3>⚙️ 주요 기능</h3>

<ul>
  <li><b>🧱 자동 벽 감지 및 생성</b>
    <ul>
      <li>OpenCV의 <code>HoughLinesP</code> 알고리즘으로 벽선을 추출합니다.</li>
      <li>모든 벽은 하나의 통합 메쉬(<code>MergedWalls</code>)로 병합되어 성능이 최적화됩니다.</li>
      <li>벽이 생성될 때 <b>부드럽게 상승하는 애니메이션</b>이 적용됩니다.</li>
    </ul>
  </li>

  <li><b>🪣 벽 내부 자동 채움</b>
    <ul>
      <li>검정색 벽선을 기준으로 내부를 자동 감지하여 <code>_floorColor</code>로 채웁니다.</li>
      <li>외부 영역은 무시되며, 내부의 작은 검정 화살표나 문자 등은 자동으로 필터링됩니다.</li>
    </ul>
  </li>

  <li><b>🏡 방 단위 바닥 생성</b>
    <ul>
      <li>벽으로 둘러싸인 각 영역을 식별하여 독립적인 바닥 메쉬를 생성합니다.</li>
      <li>결과 계층 구조는 아래와 같습니다:</li>
    </ul>

<pre><code>FloorPlan
├── FloorGroups
│   ├── Floor_00
│   ├── Floor_01
│   └── ...
└── MergedWalls
</code></pre>
  </li>

  <li><b>🎨 색상 구분</b></li>
</ul>

<table>
  <tr>
    <th>역할</th>
    <th>필드명</th>
    <th>기본값</th>
  </tr>
  <tr>
    <td>벽</td>
    <td><code>_wallColor</code></td>
    <td>🔴 빨강</td>
  </tr>
  <tr>
    <td>바닥</td>
    <td><code>_floorColor</code></td>
    <td>⚪ 회색</td>
  </tr>
</table>

<hr>

<h3>🧩 핵심 처리 순서</h3>

<ol>
  <li><b>CreateWallMap()</b> — 검정 픽셀(또는 빨강)을 벽으로 인식하여 2D 맵 <code>_wallMap</code> 생성.</li>
  <li><b>ColorFloorInsideWalls()</b> — 벽으로 둘러싸인 내부만 <code>_floorColor</code>로 채우며 외부 무시.</li>
  <li><b>DetectWallSegmentsOpenCV()</b> — OpenCV로 벽선(선분) 검출 및 병합.</li>
  <li><b>CreateRoomFloors()</b> — <code>_wallMap</code> 기반 flood-fill로 각 방별 바닥 메쉬 자동 생성.</li>
  <li><b>GenerateWallsFromSegments()</b> — 검출된 선분을 기반으로 3D 벽 메쉬 생성.</li>
</ol>

<hr>

<h3>🖼️ 결과 예시</h3>

<table>
  <tr>
    <th>원본 평면도</th>
    <th>감지 결과</th>
    <th>Unity Scene 결과</th>
  </tr>
  <tr>
    <td align="center"><img width="341" height="395" alt="image" src="https://github.com/user-attachments/assets/bb4e3363-b164-4f18-93dc-1e1fa5390bcf" /></td>
    <td align="center"><img width="374" height="399" alt="image" src="https://github.com/user-attachments/assets/9ce06f58-9473-4f1a-a217-a8fcadd384bc" /></td>
    <td align="center"><img width="457" height="417" alt="image" src="https://github.com/user-attachments/assets/3182285e-32fc-4e2b-9ce4-f915f3e2cff5" /></td>
  </tr>
</table>

<hr>

<h3>🔧 설정 예시</h3>

<table>
  <tr>
    <th>필드</th>
    <th>설명</th>
    <th>예시값</th>
  </tr>
  <tr>
    <td><code>_floorPlanImage</code></td>
    <td>평면도 Texture2D (RawImage 연결)</td>
    <td><code>RawImage (UI)</code></td>
  </tr>
  <tr>
    <td><code>_planeSize</code></td>
    <td>평면 크기 (X, Y)</td>
    <td><code>(10, 10)</code></td>
  </tr>
  <tr>
    <td><code>_wallHeight</code></td>
    <td>벽 높이</td>
    <td><code>3</code></td>
  </tr>
  <tr>
    <td><code>_wallRiseSpeed</code></td>
    <td>벽 상승 속도</td>
    <td><code>1</code></td>
  </tr>
  <tr>
    <td><code>_minRoomPixel</code></td>
    <td>방으로 인식될 최소 픽셀 수</td>
    <td><code>50</code></td>
  </tr>
</table>

<hr>

<h3>📂 출력 구조 (Hierarchy)</h3>

<pre><code>FloorPlan
├── FloorGroups
│   ├── Floor_00
│   ├── Floor_01
│   └── ...
└── MergedWalls
</code></pre>
