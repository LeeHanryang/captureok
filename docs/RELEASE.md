# 배포 절차 (만드는 사람용)

## 1. 버전 올리기

`Directory.Build.props`의 `<Version>`을 올립니다 (예: `1.0.0` → `1.0.1`).
설치 파일 이름과 Windows "설치된 앱"의 버전, 환경설정 > 정보의 버전이 모두 이 값을 따릅니다.

- 고친 것만 있으면 세 번째 자리(1.0.**1**), 기능이 늘면 두 번째 자리(1.**1**.0)

## 2. 확인

```powershell
dotnet test
.\installer\build.ps1
```

- 만든 설치 파일로 이 PC에 설치해 보고(이전 버전이 켜진 채로 덮어 설치 포함), 주요 캡처·저장·복사가 되는지 봅니다.
- 손으로 확인할 목록: `docs/manual-checklist-m*.md`

## 3. GitHub Release 만들기

```powershell
git tag v1.0.1
git push origin main --tags
# 설치 파일의 SHA-256 (릴리스 설명에 붙여 받은 파일이 맞는지 확인할 수 있게)
Get-FileHash installer\output\CaptureOK-Setup-1.0.1.exe -Algorithm SHA256
```

GitHub 저장소 > Releases > **Draft a new release** → 태그 `v1.0.1` 선택 → 설치 파일 첨부 → 바뀐 점과 SHA-256을 적고 게시.

(`gh` CLI가 있으면: `gh release create v1.0.1 installer\output\CaptureOK-Setup-1.0.1.exe --title "CaptureOK 1.0.1" --notes-file notes.md`)

## 참고: 코드 서명

지금 설치 파일은 코드 서명이 없어 받는 사람의 PC에서 "Windows의 PC 보호"(SmartScreen) 경고가 뜹니다.
없애려면 코드 서명 인증서로 `CaptureOk.exe`와 설치 파일에 서명해야 합니다 (유료 인증서 또는 오픈소스용 무료 서명 서비스).
서명하면 Inno Setup의 `SignTool` 설정으로 설치 파일 만들 때 함께 서명할 수 있습니다.
