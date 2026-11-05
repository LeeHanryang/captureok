<p align="center">
  <img src="assets/logo/icon-512.png" width="96" alt="CaptureOK 로고">
</p>

<h1 align="center">CaptureOK</h1>

<p align="center">Windows용 화면 캡처·편집 도구 — 단축키 한 번으로 찍고, 바로 표시하고, 저장·복사합니다.</p>

---

## 주요 기능

**캡처**

| 방식 | 기본 단축키 | 설명 |
|---|---|---|
| 영역 지정 캡처 | `PrtSc`, `Ctrl+Alt+R` | 드래그로 영역을 고릅니다. 마우스 아래 창·버튼·패널을 자동으로 강조해 클릭 한 번으로도 고를 수 있습니다. 가운데 버튼(휠) 클릭은 스크롤 캡처, 우클릭은 메뉴(GIF 녹화) |
| 창 캡처 | `Ctrl+Alt+W` | 창을 클릭해 그 창만 |
| 요소 캡처 | `Ctrl+Alt+E` | 창 안의 패널·목록·버튼 같은 부분만 (휠로 바깥·안쪽 요소) |
| 전체 화면 캡처 | `Ctrl+Alt+F` | 마우스가 있는 모니터 전체 |
| 고정 크기 캡처 | `Ctrl+Alt+Z` | 미리 정한 크기(최대 9개, 숫자 1~9로 전환)의 칸을 옮겨 찍기 |
| 스크롤 캡처 | `Ctrl+Alt+S`, 또는 영역 지정 중 가운데 클릭 | 스크롤되는 영역(웹 페이지 본문·목록)을 고르면 끝까지 내려가며 한 장으로 이어 붙입니다 |
| GIF 녹화 | 영역 지정·요소 캡처 중 우클릭 → GIF 녹화 | 강조된 부분이나 드래그 중인 범위(왼쪽 버튼을 누른 채 우클릭)를 움직이는 GIF로 녹화합니다 (초당 15프레임, 최대 30초, 마우스 커서 포함). 녹화 중에도 화면을 조작할 수 있고, `Esc` 또는 멈춤 버튼으로 끝냅니다 |
| 직전 영역 캡처 | `Ctrl+Alt+L` | 마지막으로 고른 영역을 다시 |

- 영역을 고르는 동안 조작 안내·십자 안내선·돋보기·크기 표시, `Ctrl`을 누르고 끌면 세밀하게, 방향키로 1픽셀씩 조정
- 여러 모니터·모니터마다 다른 화면 배율 지원, 캡처 순간의 마우스 커서 넣기(선택)
- 단축키는 `Ctrl+Alt` + 캡처 방식 영어 이름의 첫 글자(Region·Window·Element·Full screen·siZe·Scroll·Last)이고, 환경설정에서 바꿀 수 있습니다

**편집**

- 펜·형광펜·사각형·원·직선·화살표·텍스트·번호, 모자이크·블러, 자르기, 지우개, 실행 취소·다시 실행
- 펜·형광펜: `Shift`를 누르고 그리면 완전한 가로·세로 직선, `Ctrl`을 누르고 그리면 원하는 각도의 직선
- 편집 창에서 바로 `Ctrl+C` 복사, `Ctrl+S` PNG 저장 (파일 이름 규칙·저장 폴더 설정)

**캡처 목록**

- 최근 캡처를 옆 목록에 모아 두고 오가며 편집, 한 번에 모두 저장
- 고정한 캡처(최대 10개)는 프로그램을 다시 켜도 편집한 상태 그대로 돌아옵니다

**그 밖에**

- 라이트·다크 테마(시스템 따라가기), Windows 시작 시 자동 실행, 트레이 메뉴
- 저장 폴더·파일 이름 규칙·자동 저장, 캡처 목록 개수 등 환경설정

## 설치

[Releases](../../releases)에서 `CaptureOK-Setup-<버전>.exe`를 받아 실행하세요. 자세한 내용은 [사용자 설치 가이드](docs/INSTALL.md)를 봐 주세요.

- Windows 10(1809 이상)·11, 64비트
- .NET 런타임이 설치 파일에 들어 있어 따로 설치할 것이 없습니다
- 관리자 권한 없이 지금 사용자에게만 설치됩니다

## 직접 빌드하기

[.NET 10 SDK](https://dotnet.microsoft.com/download)가 필요합니다.

```powershell
dotnet build
dotnet test
dotnet run --project src/CaptureOk
```

설치 파일은 [Inno Setup 6](https://jrsoftware.org/isinfo.php)이 더 필요합니다 (`winget install JRSoftware.InnoSetup`).

```powershell
.\installer\build.ps1
```

`installer\output\CaptureOK-Setup-<버전>.exe`가 만들어집니다. 배포 절차는 [docs/RELEASE.md](docs/RELEASE.md)에 있습니다.

### 폴더 구성

| 폴더 | 내용 |
|---|---|
| `src/CaptureOk.Core` | 화면·WPF와 무관한 부분 (선택·편집 문서·이어 붙이기·설정·단축키) — 단위 테스트 대상 |
| `src/CaptureOk` | WPF 앱 (캡처 오버레이·편집 창·트레이·설정 창·Win32 호출) |
| `tests/CaptureOk.Tests` | xUnit 테스트 |
| `installer` | 설치 파일 스크립트 |
| `assets/logo` | 로고 원본과 아이콘 만드는 스크립트 |

## 만든 사람

**LeeHanryang** — https://github.com/LeeHanryang

## 라이선스

- CaptureOK: [GPL-3.0](LICENSE) © 2026 LeeHanryang
- 사용한 오픈소스(WPF UI, Fluent UI System Icons, .NET 등)의 라이선스와 저작권 표시: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)
