"""배포판의 고사양/저사양 설치 버튼: 기존 전체 설치 흐름과 오류 안내를 유지한다."""
from pathlib import Path
import argparse,os,sys,runpy,traceback

def main():
    root=Path(__file__).resolve().parents[1]
    try:
        from performance_options429 import ENV
        p=argparse.ArgumentParser(description='모델변경 모드 고사양/저사양 설치')
        p.add_argument('--mode',required=True,choices=('low','standard'))
        p.add_argument('--reinstall',action='store_true',help='모델 추가/복구를 위해 전체 설치를 다시 실행')
        a,rest=p.parse_known_args()
        os.environ[ENV]=a.mode
        if a.mode=='low':
            print('선택한 설치: 저사양 — CG 원본 화질·짧은 반복 / 모델 텍스처 절약, 모델·손·무기·오라는 매 프레임 갱신',flush=True)
        else:
            print('선택한 설치: 고사양 — 기존 텍스처 품질과 모델 갱신 빈도 유지',flush=True)
        peek=argparse.ArgumentParser(add_help=False)
        peek.add_argument('--game',type=Path);peek.add_argument('--inputs',type=Path,default=root/'원본모델_넣는곳')
        peek.add_argument('--prepare-only',action='store_true');peek.add_argument('--rollback',action='store_true')
        options,_=peek.parse_known_args(rest)
        from game_location import select_game,valid_game
        game=options.game or select_game(root)
        game=game.resolve()
        if not valid_game(game):raise ValueError('게임 설치 폴더를 찾지 못했습니다. 게임 폴더를 다시 선택해 주세요.')
        if options.game is None:rest.extend(['--game',str(game)])
        from catalog_sync458 import sync
        if not options.rollback:sync(root,game)
        from performance_install430 import new_inputs,switch_installed,remember_inputs
        if not (a.reinstall or options.prepare_only or options.rollback):
            if not new_inputs(game,options.inputs) and switch_installed(root,game,a.mode):return
        print('BepInEx 확인부터 원본 모델 준비·설치까지 진행합니다. 별도 성능 패치는 필요 없습니다.',flush=True)
        # 기존 내장 Python/의존성 검사와 한국어 오류 경계를 건너뛰지 않습니다.
        entry=root/'scripts/installer_entry.py'
        script=entry if entry.is_file() else root/'scripts/install_release.py'
        os.chdir(root)
        sys.argv=[str(script),*rest]
        try:runpy.run_path(str(script),run_name='__main__')
        except SystemExit as result:
            if result.code not in (None,0):raise
        if not (options.prepare_only or options.rollback):remember_inputs(game,options.inputs)
    except SystemExit:
        raise
    except Exception as error:
        report=root/'오류상세_문의용.txt'
        try:report.write_text(traceback.format_exc(),encoding='utf-8-sig')
        except OSError:pass
        message=str(error)
        if not any('가'<=c<='힣' for c in message):
            message='설치 파일을 실행하지 못했습니다. 배포 ZIP 전체를 풀었는지 확인해 주세요.'
        print('[설치 중단] '+message)
        print('자세한 기록:',report)
        raise SystemExit(1)

if __name__=='__main__':main()
