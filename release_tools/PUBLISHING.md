# 배포 절차

배포 원본 폴더를 수정한 뒤 `build_release.py`에 이전보다 큰 `--sequence`를 전달합니다. 최초 공개 이후에는 같은 서명 키와 `release_history.json`을 계속 사용해야 합니다. 비밀키·이력은 저장소 밖에 보관합니다.

```powershell
python release_tools/build_release.py --package "D:/배포/R419_Release_Candidate1/R419_Release_Candidate1" --repo dpo393366-tech/ModelChangeMod --sequence 459 --key "비공개폴더/signing_key.pem" --output "출력폴더"
```

개발 도구의 Python에는 `cryptography`가 필요합니다. 사용자의 게임에는 별도 설치가 필요 없습니다.

정식 GitHub Release `modelchange-459`에 다음 네 파일을 첨부합니다. 파일이 모두 올라온 다음 Draft를 해제합니다. 같은 버전의 파일을 덮어쓰지 말고 수정할 때마다 번호를 올립니다.

- `ModelChangeMod-R459-install.zip`
- `modelchange-update.zip`
- `modelchange-update.json`
- `modelchange-update.sig`

게시 성공 후 비공개 폴더의 `release_history.pending.json`을 `release_history.json`으로 반영합니다. 이 이력은 최초 배포판에서 최신 버전으로 바로 갱신할 수 있는 누적 패치와 제거 목록을 만드는 데 사용됩니다. 게시 실패 시 이력을 확정하지 않습니다.

이 저장소에 게시 권한이 있는 Git Credential Manager 계정이 연결되어 있으면 아래 명령으로 소스 커밋·푸시, 초안 릴리스 생성, 네 파일 업로드, 최종 공개와 이력 확정을 처리할 수 있습니다. 토큰과 비밀키 내용은 출력하거나 업로드하지 않습니다. 위 빌드·검사를 완료한 결과에만 실행하세요.

```powershell
python release_tools/publish_github.py --folder "출력폴더/modelchange-459" --sequence 459 --repository dpo393366-tech/ModelChangeMod --key "비공개폴더/signing_key.pem"
```

신규 캐릭터는 새 실행 코드와 지원 CG·효과를 배포 폴더에 반영하고, 원본 식별 목록·변환 레시피·`지원모델_목록.txt`를 함께 갱신합니다. 빌더가 게임용 모델 정보와 최신 레시피 목록을 자동 생성합니다. 원본 모델은 별도 배포처에 두며 본 저장소에 추가하지 않습니다.

삭제는 기존에 게시한 모드 소유 파일에 대해서만 수행됩니다. 사용자 변경 파일은 보존됩니다. 게임 저장 파일, BepInEx 공용 로더, 다른 모드 및 원본 모델은 업데이트 대상으로 지정할 수 없습니다.

검사:

```powershell
python tests/test_updater.py
```

설치 폴더의 기존 파일이 배포판과 다르다는 이유로 모드 전체를 막지 않습니다. 다운로드된 업데이트 자체의 서명·완전성 검사는 잘못 내려받은 코드가 실행되지 않도록 하기 위한 것으로, 사용자 환경 일치 검사와 다릅니다.
