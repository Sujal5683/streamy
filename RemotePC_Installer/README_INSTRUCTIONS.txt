ENTERPRISE REMOTE ACCESS - REMOTE PC SETUP GUIDE
==================================================

How to set up remote control on this computer:

1. Copy this entire 'RemotePC_Installer' folder to the target computer
   (via USB flash drive, network shared folder, or cloud drive).

2. On that target computer, simply double-click:
   '1-Click_Enroll_And_Start.bat'

3. It will automatically:
   - Enroll the computer with your central Control Plane (http://172.18.11.122:5293).
   - Generate secure hardware ECDSA identity keys.
   - Start the background Host service and desktop streaming agent.

4. On your Operator Controller computer:
   - Click the Refresh (🔄) button in the Target Device list.
   - Select this computer's name from the dropdown.
   - Click '▶ START REMOTE SESSION'!
