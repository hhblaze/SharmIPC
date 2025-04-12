rem SET MSBUILD_PATH="C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"
SET MSBUILD_PATH="C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"

%MSBUILD_PATH% "%~dp0..\Process1\SharmIpc\SharmIpc.csproj" /t:rebuild /p:Configuration=Release
rem %MSBUILD_PATH% "%~dp0..\Process1\SharmIpc\SharmIpc.csproj" /t:rebuild /p:Configuration=Release-NET462
rem %MSBUILD_PATH% "%~dp0..\Process1\SharmIpc\SharmIpc.csproj" /t:rebuild /p:Configuration=Release-NET472

%MSBUILD_PATH% "%~dp0..\Process1\SharmIpcNetStandard20\SharmIpcNetStandard20.csproj" /t:rebuild /p:Configuration=Release

%MSBUILD_PATH% "%~dp0..\Process1\SharmIpcNet6\SharmIpcNet6.csproj" /t:rebuild /p:Configuration=Release

"%~dp0nuget.exe" pack "%~dp0SharmIpc.nuspec" -BasePath "%~dp0.." -OutputDirectory "%~dp0..\__Deploy"