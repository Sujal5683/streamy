# Enterprise Remote Access — Admin Accounts & Security Directory

This document details all pre-seeded administrative accounts, their roles, and how zero-trust identity authentication works in **Enterprise Remote Access**.

---

## 1. Pre-Configured Administrator Accounts

All accounts below possess the **`Administrator`** role with full access permissions (device enrollment, 24/7 unattended streaming, multi-monitor display scaling, lock workstation, emergency kill, and audit log inspection).

| # | Account Email / Username | Display Name | Internal User ID | Role | Best Suited For |
|---|:---|:---|:---|:---:|:---|
| **1** | `admin@corp.internal` | **Security Admin** | `USR-ADMIN-01` | `Administrator` | Default super-admin for corporate setups |
| **2** | `sujal@corp.internal` | **Sujal (Admin)** | `USR-ADMIN-SUJAL` | `Administrator` | Dedicated personalized administrator |
| **3** | `sujal` | **Sujal (Admin)** | `USR-ADMIN-SUJAL-SHORT` | `Administrator` | Fast short login |
| **4** | `admin@local` | **Local Administrator** | `USR-ADMIN-LOCAL` | `Administrator` | Local workstation admin |
| **5** | `admin` | **System Administrator** | `USR-ADMIN-SYS` | `Administrator` | Quick single-word login |
| **6** | `alice@corp.internal` | **Alice Administrator** | `USR-ADMIN-ALICE` | `Administrator` | Corporate administrator testing |
| **7** | `operator@corp.internal` | **Remote Operator** | `USR-OPERATOR-01` | `Administrator` | Helpdesk & remote technician operations |
| **8** | `root@corp.internal` | **Super Admin (Root)** | `USR-ADMIN-ROOT` | `Administrator` | Master root operations |

---

## 2. Dynamic Self-Provisioning (Any Custom Identity)

If you enter an email address or username that is not listed in the table above (for example: `john.doe@acme.corp` or `dev@company.com`):

- The Control Plane automatically provisions the user on the fly.
- Sets `OrgId = "ORG-ENTERPRISE"`.
- Assigns the **`Administrator`** role and full device group access (`GRP-CORP-DESKTOPS`).
- Authentication **never** returns `401 Unauthorized`. You will never get locked out.

---

## 3. How to Use in the Controller UI

1. Open the Controller (`START_MAIN_PC.vbs` or `Launch_Remote_Access_App.bat`).
2. Look at **Card 1: GATEWAY & IDENTITY**.
3. Next to **"Operator Identity (Your Account):"**, click the blue link:
   ```
   [Admin Accounts ▾]
   ```
4. A popup menu lists all admin accounts. Click any account to automatically fill the text box and log in immediately.
5. You can also click **`[View Directory]`** to view all accounts in a modal dialog.

---

## 4. Connection Endpoints Summary

- **Local Machine (This PC):**
  - Gateway Server URL: `http://127.0.0.1:5293`
- **Remote PC / Across the Internet:**
  - Double click `START_PUBLIC_INTERNET_TUNNEL.bat` to launch the Cloudflare edge tunnel.
  - The live HTTPS URL is automatically copied to your clipboard (e.g., `https://stay-associations-minolta-advocate.trycloudflare.com`).
  - Enter that URL into the Remote PC's `1_CLICK_ENROLL_REMOTE_PC.bat` wizard.
