$base = "c:\My Projects\SECURE AND ENTERPRISE STREAMING APP\EnterpriseRemote_Apps"
New-Item -ItemType Directory -Force -Path "$base\1_MAIN_PC_CONTROLLER\Bin" | Out-Null
New-Item -ItemType Directory -Force -Path "$base\2_REMOTE_PC_STREAMER\Bin" | Out-Null
New-Item -ItemType Directory -Force -Path "RemotePC_Package" | Out-Null

# Copy Main PC files (Controller + ControlPlane + Common + logo/ico)
Copy-Item -Path "dist\Controller\*" -Destination "$base\1_MAIN_PC_CONTROLLER\Bin" -Recurse -Force
Copy-Item -Path "dist\ControlPlane\*" -Destination "$base\1_MAIN_PC_CONTROLLER\Bin" -Recurse -Force
Copy-Item -Path "src\EnterpriseRemote.Controller\app.ico" -Destination "$base\1_MAIN_PC_CONTROLLER\Bin" -Force
Copy-Item -Path "src\EnterpriseRemote.Controller\app_logo.png" -Destination "$base\1_MAIN_PC_CONTROLLER\Bin" -Force

# Copy Remote PC files (HostService + UserAgent + Common + ico)
Copy-Item -Path "dist\HostService\*" -Destination "$base\2_REMOTE_PC_STREAMER\Bin" -Recurse -Force
Copy-Item -Path "dist\UserAgent\*" -Destination "$base\2_REMOTE_PC_STREAMER\Bin" -Recurse -Force
Copy-Item -Path "src\EnterpriseRemote.Controller\app.ico" -Destination "$base\2_REMOTE_PC_STREAMER\Bin" -Force

# Also update RemotePC_Package
Copy-Item -Path "dist\HostService\*" -Destination "RemotePC_Package" -Recurse -Force
Copy-Item -Path "dist\UserAgent\*" -Destination "RemotePC_Package" -Recurse -Force
Copy-Item -Path "src\EnterpriseRemote.Controller\app.ico" -Destination "RemotePC_Package" -Force
Copy-Item -Path "src\EnterpriseRemote.Controller\app_logo.png" -Destination "RemotePC_Package" -Force

Write-Host "Copied all binaries to EnterpriseRemote_Apps and RemotePC_Package successfully!"
