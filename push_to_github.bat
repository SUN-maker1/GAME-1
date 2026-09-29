@echo off
setlocal enabledelayedexpansion

echo ============================================
echo        Git Auto Push Script
echo   Repo: https://github.com/SUN-maker1/GAME-1.git
echo ============================================
echo.

REM ---------- Step 0: Set Git global user config ----------
echo [Step 0/4] Setting Git global user.name and user.email ...
git config --global user.name "SUN-maker1"
set "ec1=!errorlevel!"
git config --global user.email "1264257086@qq.com"
set "ec2=!errorlevel!"
if "!ec1!"=="0" if "!ec2!"=="0" (
    echo [OK] user.name  = SUN-maker1
    echo [OK] user.email = 1264257086@qq.com
) else (
    echo [WARN] Failed to set git config. ec_name=!ec1! ec_email=!ec2!
    echo        Try running these two commands manually in terminal:
    echo          git config --global user.name "SUN-maker1"
    echo          git config --global user.email "1264257086@qq.com"
)
echo.

REM ---------- Step 1: git add . ----------
echo [Step 1/4] Running: git add .
echo.
git add .
set "ec=!errorlevel!"
if not "!ec!"=="0" (
    echo [FAIL] git add . failed! Errorcode: !ec!
    echo [Reason] Current folder is not a Git repo, or files are locked / no permission.
    echo.
    pause
    exit /b 1
)
echo [OK] git add . done. All changes staged.
echo.

REM ---------- Step 2: input commit message ----------
echo [Step 2/4] Please type your commit message:
set /p "commit_msg=  Message: "
if "!commit_msg!"=="" (
    echo [FAIL] Commit message is empty. Cancelled.
    echo.
    pause
    exit /b 1
)
echo.

REM ---------- Step 3: git commit ----------
echo [Step 3/4] Running: git commit -m "!commit_msg!"
echo.
git commit -m "!commit_msg!"
set "ec=!errorlevel!"
if "!ec!"=="0" (
    echo [OK] git commit done.
) else if "!ec!"=="1" (
    echo [INFO] Nothing to commit - no changes since last commit.
    echo        Will still try to push to remote.
) else (
    echo [FAIL] git commit failed! Errorcode: !ec!
    echo [Reason] Git user.name/user.email not configured, or there are conflict files.
    echo.
    pause
    exit /b 1
)
echo.

REM ---------- Step 4: git push ----------
echo [Step 4/4] Pushing to https://github.com/SUN-maker1/GAME-1.git ...
echo           If a login window pops up, enter your GitHub username
echo           and your Personal Access Token (NOT your password).
echo.
git push https://github.com/SUN-maker1/GAME-1.git
set "ec=!errorlevel!"
if not "!ec!"=="0" (
    echo.
    echo [FAIL] git push failed! Errorcode: !ec!
    echo [Possible reasons]
    echo   1. Network cannot reach github.com
    echo   2. Auth failed - need Personal Access Token, not password
    echo   3. Remote has new commits - run: git pull https://github.com/SUN-maker1/GAME-1.git --rebase
    echo   4. Wrong repo URL or no write permission
) else (
    echo.
    echo ============================================
    echo   [DONE] Code pushed to GitHub successfully!
    echo ============================================
)
echo.
pause
endlocal