echo mml2vgm

rem Delete old output files
del /Q .\output\*.*
del /Q .\output\mml2vgmx64\*.*
del /Q .\output\mml2vgmx64\fnum\*.*
del /Q .\output\mml2vgmx64\lang\*.*
del /Q .\output\samples\*.*

rem Copy mml2vgmx64 (lightweight UI version)
xcopy .\mml2vgmx64\bin\Release\net10.0-windows\*.* .\output\mml2vgmx64 /E /R /Y /I /K
rem Copy mvcx64 (command-line version)
xcopy .\mvcx64\bin\Release\net10.0\*.* .\output\mml2vgmx64 /E /R /Y /I /K
rem Copy sample files
xcopy .\samples\*.* .\output\samples /E /R /Y /I /K
rem Copy mml2vgmIDEx64
xcopy .\mml2vgmIDEx64\bin\x64\Release\net10.0-windows7.0\*.* .\output\mml2vgmIDEx64 /E /R /Y /I /K

copy /Y .\CHANGE.txt .\output
copy /Y .\..\LICENSE.txt .\output
copy /Y .\..\mml2vgm_MMLCommandMemo.txt .\output
copy /Y .\..\mml2vgm_MMLCommandMemo.txt .\output\mml2vgmIDEx64
copy /Y .\..\mmlCommandTable.md .\output
copy /Y .\..\README.md .\output
copy /Y .\removeZoneIdent.bat .\output
if exist .\output\OtherDocuments rmdir /S /Q .\output\OtherDocuments
xcopy .\OtherDocuments .\output\OtherDocuments /E /R /Y /I /K

del /Q .\output\mml2vgmx64\*.pdb
del /Q .\output\mml2vgmx64\*.wav

del /Q .\output\mml2vgmIDEx64\*.pdb
del /Q .\output\mml2vgmIDEx64\scci.dll
del /Q .\output\mml2vgmIDEx64\scciconfig.exe
del /Q .\output\mml2vgmIDEx64\*.wav

del /Q .\output\mml2vgm_custom.zip
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '.\output\*' -DestinationPath '.\output\mml2vgm_custom.zip' -Force"

pause
