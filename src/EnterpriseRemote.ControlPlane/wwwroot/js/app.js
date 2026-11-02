/**
 * Enterprise Remote Access — Modern Web & Desktop Unified Controller
 * High-Performance Session Orchestration, Endpoints Inventory,
 * Advanced Settings, Screen/Desktop Switcher, Collapsible Sidebar, and Fullscreen HUD.
 */

import { RemoteStreamClient } from './stream.js';
import { InputHandler } from './input.js';

class WebControllerApp {
    constructor() {
        this.serverUrl = localStorage.getItem('ER_GATEWAY_URL') || window.location.origin;
        if (!this.serverUrl || this.serverUrl === 'null' || this.serverUrl.startsWith('file:')) {
            this.serverUrl = 'http://127.0.0.1:5293';
        }

        this.currentUser = null;
        this.devices = [];
        this.selectedDevice = null;
        this.activeSession = null;
        this.permanentTokens = this.loadPermanentTokens();

        // Stream and Input Engines
        this.canvas = document.getElementById('viewportCanvas');
        this.streamClient = new RemoteStreamClient(this.canvas, {
            onStatusChange: (status, message) => this.onStreamStatusChange(status, message),
            onTelemetry: (telemetry) => this.onTelemetryUpdate(telemetry),
            onCursorChange: (cursor) => this.onCursorChange(cursor),
            onDesktopList: (list) => this.onDesktopListUpdate(list),
            onError: (err) => this.showToast(err, 'error'),
            onReauthorizeNeeded: () => this.autoReconnectSession()
        });
        this.inputHandler = new InputHandler(this.canvas, this.streamClient);

        // Screen & Display State
        this.selectedDisplayIndex = 0;
        this.selectedDesktopName = 'Default';
        this.isAutoFollow = true;
        this.availableDisplays = [{ index: 0, name: 'Display 1 (Primary)', isPrimary: true }];
        this.availableDesktops = ['Default', 'Winlogon', 'SafeExamBrowser'];

        // Fullscreen Island HUD State
        this.isIslandPinned = true;
        this.isIslandCollapsed = false;
        this.islandAutoHideTimer = null;

        // Clipboard Sync State
        this.clipboardSyncEnabled = true;
        this.lastClipboardText = '';

        // UI Element References
        this.dom = {
            mainHeader: document.getElementById('main-header'),
            mainToolbar: document.getElementById('main-toolbar'),
            userSelect: document.getElementById('select-user'),
            deviceSelect: document.getElementById('select-device'),
            keyInput: document.getElementById('input-permanent-key'),
            btnToggleKeyVis: document.getElementById('btn-toggle-key-visibility'),
            btnConnect: document.getElementById('btn-connect'),
            btnDisconnect: document.getElementById('btn-disconnect'),
            btnToggleInput: document.getElementById('btn-toggle-input'),
            labelToggleInput: document.getElementById('label-toggle-input'),
            btnFullscreen: document.getElementById('btn-fullscreen'),
            statusBadge: document.getElementById('status-badge'),
            statusText: document.getElementById('status-text'),
            placeholder: document.getElementById('viewport-placeholder'),
            btnPlaceholderEnroll: document.getElementById('btn-placeholder-enroll'),
            btnPlaceholderConn: document.getElementById('btn-placeholder-connections'),

            // Header Elements
            headerTargetChip: document.getElementById('header-target-chip'),
            headerTargetName: document.getElementById('header-target-name'),
            btnHeaderMore: document.getElementById('btn-header-more'),
            menuHeaderMore: document.getElementById('menu-header-more'),
            btnHeaderAdminDir: document.getElementById('btn-header-admin-dir'),
            btnHeaderKeypair: document.getElementById('btn-header-keypair'),
            btnHeaderAudit: document.getElementById('btn-header-audit'),
            btnHeaderPolicy: document.getElementById('btn-header-policy'),

            // Custom Cyber Selects
            btnTriggerUser: document.getElementById('btn-trigger-user'),
            labelTriggerUser: document.getElementById('label-trigger-user'),
            badgeTriggerUser: document.getElementById('badge-trigger-user'),
            menuCustomUser: document.getElementById('menu-custom-user'),

            btnTriggerDevice: document.getElementById('btn-trigger-device'),
            labelTriggerDevice: document.getElementById('label-trigger-device'),
            badgeTriggerDevice: document.getElementById('badge-trigger-device'),
            menuCustomDevice: document.getElementById('menu-custom-device'),

            // Telemetry HUD
            telemetryFps: document.getElementById('telemetry-fps'),
            telemetryRtt: document.getElementById('telemetry-rtt'),
            telemetryRes: document.getElementById('telemetry-res'),
            telemetryBitrate: document.getElementById('telemetry-bitrate'),

            // Toolbar Scaling & Profiles
            btnScaleToggle: document.getElementById('btn-scale-toggle'),
            labelScaleMode: document.getElementById('label-scale-mode'),
            scaleFit: document.getElementById('btn-scale-fit'),
            scaleStretch: document.getElementById('btn-scale-stretch'),
            scaleOriginal: document.getElementById('btn-scale-original'),
            profileLan: document.getElementById('btn-profile-lan'),
            profileWan: document.getElementById('btn-profile-wan'),
            profileSharp: document.getElementById('btn-profile-sharp'),

            // Screen Selector
            screenSelectorBtn: document.getElementById('btn-screen-selector'),
            screenSelectorLabel: document.getElementById('label-screen-selector'),
            screenSelectorMenu: document.getElementById('menu-screen-selector'),

            // Quick Keys & Stream Config Popover
            quickActionsBtn: document.getElementById('btn-quick-actions'),
            quickActionsMenu: document.getElementById('menu-quick-actions'),
            configBtn: document.getElementById('btn-config-popover'),
            configMenu: document.getElementById('menu-config-popover'),
            sliderFps: document.getElementById('slider-fps'),
            sliderFpsVal: document.getElementById('val-slider-fps'),
            sliderQuality: document.getElementById('slider-quality'),
            sliderQualityVal: document.getElementById('val-slider-quality'),
            selectHeight: document.getElementById('select-max-height'),
            valSliderResBadge: document.getElementById('val-slider-res-badge'),
            btnRes720: document.getElementById('btn-res-720'),
            btnRes1080: document.getElementById('btn-res-1080'),
            btnResNative: document.getElementById('btn-res-native'),

            // Gateway Chip
            gatewayChip: document.getElementById('btn-gateway-chip'),
            gatewayChipText: document.getElementById('gateway-chip-text'),

            // Modals Triggers
            btnOpenConnections: document.getElementById('btn-open-connections'),
            btnOpenSettings: document.getElementById('btn-open-settings'),
            modalBackdrop: document.getElementById('modal-backdrop'),
            modalConnections: document.getElementById('modal-connections'),
            modalSettings: document.getElementById('modal-settings'),
            btnCloseConnections: document.getElementById('btn-close-connections'),
            btnCloseSettings: document.getElementById('btn-close-settings'),

            // Floating Island Controls
            floatingIsland: document.getElementById('floating-island-bar'),
            floatStatusPill: document.getElementById('float-status-pill'),
            floatDeviceName: document.getElementById('float-device-name'),
            btnFloatInput: document.getElementById('btn-float-input'),
            btnFloatScreens: document.getElementById('btn-float-screens'),
            btnFloatQuick: document.getElementById('btn-float-quick'),
            btnFloatScale: document.getElementById('btn-float-scale'),
            btnFloatSettings: document.getElementById('btn-float-settings'),
            btnFloatPin: document.getElementById('btn-float-pin'),
            btnFloatCollapse: document.getElementById('btn-float-collapse'),
            btnFloatExitFullscreen: document.getElementById('btn-float-exit-fullscreen'),
            btnFloatDisconnect: document.getElementById('btn-float-disconnect'),
            floatEdgeTrigger: document.getElementById('float-edge-trigger'),

            // Connections Modal Elements
            connectionsGrid: document.getElementById('connections-device-list'),
            btnRefreshDevicesModal: document.getElementById('btn-modal-refresh-devices'),
            wizardCode: document.getElementById('wizard-invitation-code'),
            wizardPin: document.getElementById('wizard-quick-pin'),
            wizardCmd: document.getElementById('wizard-powershell-cmd'),
            btnCopyCode: document.getElementById('btn-wizard-copy-code'),
            btnRefreshCode: document.getElementById('btn-wizard-refresh-code'),
            btnCopyCmd: document.getElementById('btn-wizard-copy-cmd'),
            chipWifi: document.getElementById('chip-set-wifi'),
            chipLocalhost: document.getElementById('chip-set-localhost'),
            chipTunnel: document.getElementById('chip-set-tunnel'),
            inputCustomGateway: document.getElementById('input-custom-gateway'),
            btnApplyGateway: document.getElementById('btn-apply-custom-gateway'),
            gatewayStatusVal: document.getElementById('gateway-status-val'),
            gatewayLanVal: document.getElementById('gateway-lan-val'),

            // Cloudflare Tunnel Launcher Elements
            tunnelLiveBadge: document.getElementById('tunnel-live-badge'),
            tunnelActiveRow: document.getElementById('tunnel-active-row'),
            tunnelActiveUrl: document.getElementById('tunnel-active-url'),
            btnUseActiveTunnel: document.getElementById('btn-use-active-tunnel'),
            btnLaunchTunnelTerminal: document.getElementById('btn-launch-tunnel-terminal'),
            btnCopyTunnelCmd: document.getElementById('btn-copy-tunnel-cmd'),
            inputTunnelUrlManual: document.getElementById('input-tunnel-url-manual'),
            btnApplyTunnelManual: document.getElementById('btn-apply-tunnel-manual'),
            btnSidebarLaunchTunnel: document.getElementById('btn-sidebar-launch-tunnel'),

            // Settings Modal Elements
            modalSliderFps: document.getElementById('modal-slider-fps'),
            modalValFps: document.getElementById('modal-val-fps'),
            modalSliderQuality: document.getElementById('modal-slider-quality'),
            modalValQuality: document.getElementById('modal-val-quality'),
            modalSelectRes: document.getElementById('modal-select-resolution'),
            modalToggleLowLatency: document.getElementById('modal-toggle-low-latency'),
            modalToggleInput: document.getElementById('modal-toggle-input'),
            modalToggleClipboard: document.getElementById('modal-toggle-clipboard'),
            btnEmergencyKill: document.getElementById('btn-emergency-kill'),
            accountsCardsList: document.getElementById('accounts-cards-list'),

            // Diagnostics Elements
            diagHostname: document.getElementById('diag-hostname'),
            diagDeviceId: document.getElementById('diag-device-id'),
            diagSessionId: document.getElementById('diag-session-id'),
            diagGatewayUrl: document.getElementById('diag-gateway-url'),
            diagRtt: document.getElementById('diag-rtt'),
            diagFrames: document.getElementById('diag-frames'),
            diagBytes: document.getElementById('diag-bytes'),

            // Collapsible Left Sidebar Elements
            btnToggleSidebar: document.getElementById('btn-toggle-sidebar'),
            btnCollapseSidebar: document.getElementById('btn-sidebar-collapse'),
            sidePanel: document.getElementById('side-panel'),
            sidebarTxtServer: document.getElementById('sidebar-txt-server'),
            btnSidebarApplyServer: document.getElementById('btn-sidebar-apply-server'),
            sidebarGatewayModeBadge: document.getElementById('sidebar-gateway-mode-badge'),
            sidebarSelectUser: document.getElementById('sidebar-select-user'),
            btnSidebarAdminMenu: document.getElementById('btn-sidebar-admin-menu'),
            menuSidebarAdminAccounts: document.getElementById('menu-sidebar-admin-accounts'),
            sidebarChipWifi: document.getElementById('sidebar-chip-wifi'),
            sidebarChipLocalhost: document.getElementById('sidebar-chip-localhost'),
            sidebarChipTunnel: document.getElementById('sidebar-chip-tunnel'),
            sidebarSelectDevice: document.getElementById('sidebar-select-device'),
            btnSidebarRefreshDevices: document.getElementById('btn-sidebar-refresh-devices'),
            sidebarTxtKey: document.getElementById('sidebar-txt-key'),
            btnSidebarToggleKey: document.getElementById('btn-sidebar-toggle-key'),
            btnSidebarEnroll: document.getElementById('btn-sidebar-enroll'),
            sidebarToggleInput: document.getElementById('sidebar-toggle-input'),
            sidebarToggleClipboard: document.getElementById('sidebar-toggle-clipboard'),
            btnSidebarConnect: document.getElementById('btn-sidebar-connect'),
            btnSidebarDisconnect: document.getElementById('btn-sidebar-disconnect'),
            sidebarHudTarget: document.getElementById('sidebar-hud-target'),
            sidebarHudStatus: document.getElementById('sidebar-hud-status'),
            sidebarHudRtt: document.getElementById('sidebar-hud-rtt'),
            sidebarHudFps: document.getElementById('sidebar-hud-fps'),
            sidebarHudBitrate: document.getElementById('sidebar-hud-bitrate'),
            sidebarHudRes: document.getElementById('sidebar-hud-res'),

            // Permanent Master Keys DOM
            tabPermanentKeys: document.getElementById('tab-permanent-keys'),
            txtMasterKeyName: document.getElementById('txt-master-key-name'),
            txtMasterKeyVal: document.getElementById('txt-master-key-val'),
            btnGenMasterKey: document.getElementById('btn-generate-master-key-val'),
            txtMasterKeyDesc: document.getElementById('txt-master-key-desc'),
            btnSaveMasterKey: document.getElementById('btn-save-master-key'),
            masterKeysContainer: document.getElementById('master-keys-list-container'),
            btnRefreshMasterKeys: document.getElementById('btn-refresh-master-keys'),
            btnHeaderManageKeys: document.getElementById('btn-header-manage-keys'),
            btnSidebarManageKeys: document.getElementById('btn-sidebar-manage-keys'),
            btnSettingsOpenMasterKeys: document.getElementById('btn-settings-open-master-keys')
        };

        // Restore sidebar state
        this.isSidebarCollapsed = localStorage.getItem('ER_SIDEBAR_COLLAPSED') === 'true';
        if (this.isSidebarCollapsed && this.dom.sidePanel) {
            this.dom.sidePanel.classList.add('collapsed');
        }

        // Restore saved master key or token into key inputs immediately
        const initialKey = localStorage.getItem('erp_active_master_key') || localStorage.getItem('erp_saved_key') || '';
        if (initialKey) {
            if (this.dom.keyInput) this.dom.keyInput.value = initialKey;
            if (this.dom.sidebarTxtKey) this.dom.sidebarTxtKey.value = initialKey;
        }

        // Restore server URL inputs immediately
        if (this.dom.sidebarTxtServer) this.dom.sidebarTxtServer.value = this.serverUrl;
        if (this.dom.inputCustomGateway) this.dom.inputCustomGateway.value = this.serverUrl;

        this.initCanvasSize();
        this.bindEvents();
        this.initAuthAndDevices();
        this.initClipboardListener();
        this.updateGatewayChip();
        this.initNetworkListeners();
    }

    loadPermanentTokens() {
        try {
            return JSON.parse(localStorage.getItem('ER_PERMANENT_TOKENS')) || {};
        } catch {
            return {};
        }
    }

    savePermanentToken(deviceId, token) {
        if (!deviceId) return;
        if (token && token.trim()) {
            this.permanentTokens[deviceId] = token.trim();
        } else {
            delete this.permanentTokens[deviceId];
        }
        localStorage.setItem('ER_PERMANENT_TOKENS', JSON.stringify(this.permanentTokens));
    }

    toggleSidebar(forceState = null) {
        if (!this.dom.sidePanel) return;
        const isCollapsedNow = this.dom.sidePanel.classList.contains('collapsed');
        const next = forceState !== null ? forceState : !isCollapsedNow;

        this.dom.sidePanel.classList.toggle('collapsed', next);
        this.isSidebarCollapsed = next;
        localStorage.setItem('ER_SIDEBAR_COLLAPSED', next ? 'true' : 'false');

        // Allow CSS transition to finish then recalculate canvas size
        setTimeout(() => {
            const dpr = window.devicePixelRatio || 1;
            const w = this.canvas.clientWidth;
            const h = this.canvas.clientHeight;
            if (this.canvas.width !== Math.round(w * dpr) || this.canvas.height !== Math.round(h * dpr)) {
                this.canvas.width = Math.round(w * dpr);
                this.canvas.height = Math.round(h * dpr);
                if (this.streamClient.latestBitmap) {
                    this.streamClient.drawFrame(this.streamClient.latestBitmap);
                }
            }
        }, 260);
    }

    initCanvasSize() {
        const updateSize = () => {
            const dpr = window.devicePixelRatio || 1;
            const w = this.canvas.clientWidth;
            const h = this.canvas.clientHeight;
            if (this.canvas.width !== Math.round(w * dpr) || this.canvas.height !== Math.round(h * dpr)) {
                this.canvas.width = Math.round(w * dpr);
                this.canvas.height = Math.round(h * dpr);
                if (this.streamClient.latestBitmap) {
                    this.streamClient.drawFrame(this.streamClient.latestBitmap);
                }
            }
        };

        window.addEventListener('resize', updateSize);
        setTimeout(updateSize, 100);
    }

    bindEvents() {
        // Modals Management Helpers
        const openModal = (modal) => {
            if (!modal) return;
            this.dom.modalBackdrop.classList.add('show');
            modal.classList.add('show');
        };
        const closeModals = () => {
            this.dom.modalBackdrop.classList.remove('show');
            this.dom.modalConnections.classList.remove('show');
            this.dom.modalSettings.classList.remove('show');
        };

        // Dropdown Toggles Helper
        const toggleMenu = (btn, menu) => {
            if (!menu) return;
            const isShowing = menu.classList.contains('show');
            document.querySelectorAll('.dropdown-menu').forEach(m => m.classList.remove('show'));
            if (!isShowing) menu.classList.add('show');
        };

        // Header More Options Menu (Three Dots)
        if (this.dom.btnHeaderMore && this.dom.menuHeaderMore) {
            this.dom.btnHeaderMore.addEventListener('click', (e) => {
                e.stopPropagation();
                toggleMenu(this.dom.btnHeaderMore, this.dom.menuHeaderMore);
            });
        }

        if (this.dom.btnHeaderAdminDir) {
            this.dom.btnHeaderAdminDir.addEventListener('click', () => {
                openModal(this.dom.modalSettings);
                this.switchModalTab(this.dom.modalSettings, 'tab-settings-accounts');
            });
        }
        if (this.dom.btnHeaderKeypair) {
            this.dom.btnHeaderKeypair.addEventListener('click', () => {
                openModal(this.dom.modalSettings);
                this.switchModalTab(this.dom.modalSettings, 'tab-settings-diagnostics');
                this.showToast('Controller cryptographic keypair active (ECDSA P-256)', 'info');
            });
        }
        if (this.dom.btnHeaderAudit) {
            this.dom.btnHeaderAudit.addEventListener('click', () => {
                openModal(this.dom.modalSettings);
                this.switchModalTab(this.dom.modalSettings, 'tab-settings-diagnostics');
            });
        }
        if (this.dom.btnHeaderPolicy) {
            this.dom.btnHeaderPolicy.addEventListener('click', () => {
                openModal(this.dom.modalSettings);
                this.switchModalTab(this.dom.modalSettings, 'tab-settings-input');
            });
        }

        // Sidebar Toggle Buttons & Shortcut
        if (this.dom.btnToggleSidebar) {
            this.dom.btnToggleSidebar.addEventListener('click', () => this.toggleSidebar());
        }
        if (this.dom.btnCollapseSidebar) {
            this.dom.btnCollapseSidebar.addEventListener('click', () => this.toggleSidebar(true));
        }
        window.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'b') {
                e.preventDefault();
                this.toggleSidebar();
            }
        });

        // Connect / Disconnect Buttons (Both Header & Sidebar)
        this.dom.btnConnect.addEventListener('click', () => this.startSession());
        this.dom.btnDisconnect.addEventListener('click', () => this.stopSession());
        if (this.dom.btnSidebarConnect) {
            this.dom.btnSidebarConnect.addEventListener('click', () => this.startSession());
        }
        if (this.dom.btnSidebarDisconnect) {
            this.dom.btnSidebarDisconnect.addEventListener('click', () => this.stopSession());
        }

        // Key Visibility Toggle
        this.dom.btnToggleKeyVis.addEventListener('click', () => {
            const isPass = this.dom.keyInput.type === 'password';
            this.dom.keyInput.type = isPass ? 'text' : 'password';
            if (this.dom.sidebarTxtKey) this.dom.sidebarTxtKey.type = isPass ? 'text' : 'password';
        });

        if (this.dom.btnSidebarToggleKey) {
            this.dom.btnSidebarToggleKey.addEventListener('click', () => {
                const isPass = this.dom.sidebarTxtKey.type === 'password';
                this.dom.sidebarTxtKey.type = isPass ? 'text' : 'password';
                this.dom.keyInput.type = isPass ? 'text' : 'password';
            });
        }

        // Key Input Changes (Sync Header, Sidebar & Local Storage Persistence)
        const onKeyInputChanged = (val) => {
            const raw = val || '';
            const trimmed = raw.trim();
            if (this.dom.keyInput && this.dom.keyInput.value !== raw) this.dom.keyInput.value = raw;
            if (this.dom.sidebarTxtKey && this.dom.sidebarTxtKey.value !== raw) this.dom.sidebarTxtKey.value = raw;
            if (trimmed) {
                localStorage.setItem('erp_saved_key', trimmed);
            }
            if (this.selectedDevice) {
                this.savePermanentToken(this.selectedDevice.deviceId, trimmed);
            }
        };

        this.dom.keyInput.addEventListener('input', () => onKeyInputChanged(this.dom.keyInput.value));
        if (this.dom.sidebarTxtKey) {
            this.dom.sidebarTxtKey.addEventListener('input', () => onKeyInputChanged(this.dom.sidebarTxtKey.value));
        }

        // User Selection Change (Sync Header & Sidebar)
        const onUserChange = (val) => {
            this.dom.userSelect.value = val;
            if (this.dom.sidebarSelectUser) this.dom.sidebarSelectUser.value = val;
            if (this.dom.labelTriggerUser) {
                this.dom.labelTriggerUser.textContent = `👑 ${val}`;
            }
            if (this.dom.badgeTriggerUser) {
                this.dom.badgeTriggerUser.textContent = val.includes('operator') ? 'Operator' : 'Admin';
            }
            if (this.dom.menuCustomUser) {
                this.dom.menuCustomUser.querySelectorAll('.dropdown-item').forEach(b => {
                    b.classList.toggle('active', b.getAttribute('data-email') === val);
                });
            }
            this.authenticateUser(val);
        };
        this.dom.userSelect.addEventListener('change', () => onUserChange(this.dom.userSelect.value));
        if (this.dom.sidebarSelectUser) {
            this.dom.sidebarSelectUser.addEventListener('change', () => onUserChange(this.dom.sidebarSelectUser.value));
        }

        // Custom Operator Identity Selector inside Three-Dot Menu
        if (this.dom.btnTriggerUser && this.dom.menuCustomUser) {
            this.dom.btnTriggerUser.addEventListener('click', (e) => {
                e.stopPropagation();
                const isShowing = this.dom.menuCustomUser.classList.contains('show');
                if (this.dom.menuCustomDevice) this.dom.menuCustomDevice.classList.remove('show');
                if (this.dom.btnTriggerDevice) this.dom.btnTriggerDevice.classList.remove('active');
                
                this.dom.menuCustomUser.classList.toggle('show', !isShowing);
                this.dom.btnTriggerUser.classList.toggle('active', !isShowing);
            });

            this.dom.menuCustomUser.addEventListener('click', (e) => {
                const item = e.target.closest('.dropdown-item');
                if (!item) return;
                e.stopPropagation();
                const email = item.getAttribute('data-email');
                if (email) {
                    onUserChange(email);
                    this.dom.menuCustomUser.querySelectorAll('.dropdown-item').forEach(i => i.classList.remove('active'));
                    item.classList.add('active');
                    this.dom.menuCustomUser.classList.remove('show');
                    this.dom.btnTriggerUser.classList.remove('active');
                    this.showToast(`Switched operator identity to ${email}`, 'success');
                }
            });
        }

        // Sidebar Admin Accounts Menu
        if (this.dom.btnSidebarAdminMenu && this.dom.menuSidebarAdminAccounts) {
            this.dom.btnSidebarAdminMenu.addEventListener('click', (e) => {
                e.stopPropagation();
                toggleMenu(this.dom.btnSidebarAdminMenu, this.dom.menuSidebarAdminAccounts);
            });

            this.dom.menuSidebarAdminAccounts.querySelectorAll('.dropdown-item[data-email]').forEach(btn => {
                btn.addEventListener('click', () => {
                    const email = btn.getAttribute('data-email');
                    onUserChange(email);
                    this.dom.menuSidebarAdminAccounts.classList.remove('show');
                    this.showToast(`Switched operator identity to ${email}`, 'success');
                });
            });
        }

        // Device Selection Change (Sync Header & Sidebar & Persist)
        const onDeviceChange = (devId) => {
            this.dom.deviceSelect.value = devId;
            if (this.dom.sidebarSelectDevice) this.dom.sidebarSelectDevice.value = devId;
            this.selectedDevice = this.devices.find(d => d.deviceId === devId) || null;
            if (devId) {
                localStorage.setItem('erp_last_selected_device', devId);
            }
            this.syncKeyForSelectedDevice();
            this.updateHeaderTargetDisplay();
        };

        // Custom Target Device Selector inside Three-Dot Menu
        if (this.dom.btnTriggerDevice && this.dom.menuCustomDevice) {
            this.dom.btnTriggerDevice.addEventListener('click', (e) => {
                e.stopPropagation();
                const isShowing = this.dom.menuCustomDevice.classList.contains('show');
                if (this.dom.menuCustomUser) this.dom.menuCustomUser.classList.remove('show');
                if (this.dom.btnTriggerUser) this.dom.btnTriggerUser.classList.remove('active');

                this.dom.menuCustomDevice.classList.toggle('show', !isShowing);
                this.dom.btnTriggerDevice.classList.toggle('active', !isShowing);
            });

            this.dom.menuCustomDevice.addEventListener('click', (e) => {
                const item = e.target.closest('.dropdown-item');
                if (!item) return;
                e.stopPropagation();
                const devId = item.getAttribute('data-device-id');
                if (devId) {
                    onDeviceChange(devId);
                    this.dom.menuCustomDevice.querySelectorAll('.dropdown-item').forEach(i => i.classList.remove('active'));
                    item.classList.add('active');
                    this.dom.menuCustomDevice.classList.remove('show');
                    this.dom.btnTriggerDevice.classList.remove('active');
                }
            });
        }

        this.dom.deviceSelect.addEventListener('change', () => onDeviceChange(this.dom.deviceSelect.value));
        if (this.dom.sidebarSelectDevice) {
            this.dom.sidebarSelectDevice.addEventListener('change', () => onDeviceChange(this.dom.sidebarSelectDevice.value));
        }
        if (this.dom.btnSidebarRefreshDevices) {
            this.dom.btnSidebarRefreshDevices.addEventListener('click', () => {
                this.refreshDeviceList();
                this.showToast('Refreshing device list...', 'info');
            });
        }

        // Sidebar Enroll Host Button
        if (this.dom.btnSidebarEnroll) {
            this.dom.btnSidebarEnroll.addEventListener('click', () => {
                openModal(this.dom.modalConnections);
                this.switchModalTab(this.dom.modalConnections, 'tab-enroll-new');
                this.generateEnrollmentCode();
            });
        }

        // Permanent Master Keys Modal Triggers & Controls
        if (this.dom.btnSidebarManageKeys) {
            this.dom.btnSidebarManageKeys.addEventListener('click', () => {
                openModal(this.dom.modalConnections);
                this.switchModalTab(this.dom.modalConnections, 'tab-permanent-keys');
            });
        }
        if (this.dom.btnHeaderManageKeys) {
            this.dom.btnHeaderManageKeys.addEventListener('click', () => {
                openModal(this.dom.modalConnections);
                this.switchModalTab(this.dom.modalConnections, 'tab-permanent-keys');
            });
        }
        if (this.dom.btnSettingsOpenMasterKeys) {
            this.dom.btnSettingsOpenMasterKeys.addEventListener('click', () => {
                this.dom.modalSettings?.classList.remove('show');
                openModal(this.dom.modalConnections);
                this.switchModalTab(this.dom.modalConnections, 'tab-permanent-keys');
            });
        }
        if (this.dom.btnGenMasterKey) {
            this.dom.btnGenMasterKey.addEventListener('click', () => {
                const randomPart = Math.random().toString(36).substring(2, 10).toUpperCase();
                if (this.dom.txtMasterKeyVal) {
                    this.dom.txtMasterKeyVal.value = `PERM-MASTER-${randomPart}`;
                }
            });
        }
        if (this.dom.btnSaveMasterKey) {
            this.dom.btnSaveMasterKey.addEventListener('click', () => this.saveMasterKey());
        }
        if (this.dom.btnRefreshMasterKeys) {
            this.dom.btnRefreshMasterKeys.addEventListener('click', () => this.loadMasterKeys());
        }

        // Sidebar Server Input & Chips
        if (this.dom.btnSidebarApplyServer) {
            this.dom.btnSidebarApplyServer.addEventListener('click', () => {
                this.applyGatewayUrl(this.dom.sidebarTxtServer.value.trim());
            });
        }
        if (this.dom.sidebarTxtServer) {
            this.dom.sidebarTxtServer.addEventListener('keypress', (e) => {
                if (e.key === 'Enter') {
                    this.applyGatewayUrl(this.dom.sidebarTxtServer.value.trim());
                }
            });
            this.dom.sidebarTxtServer.addEventListener('change', () => {
                const val = this.dom.sidebarTxtServer.value.trim();
                if (val && val !== this.serverUrl) {
                    this.applyGatewayUrl(val);
                }
            });
        }
        if (this.dom.sidebarChipWifi) {
            this.dom.sidebarChipWifi.addEventListener('click', () => {
                const lanIp = this.dom.gatewayLanVal.textContent || '192.168.1.1';
                this.applyGatewayUrl(`http://${lanIp}:5293`);
            });
        }
        if (this.dom.sidebarChipLocalhost) {
            this.dom.sidebarChipLocalhost.addEventListener('click', () => {
                this.applyGatewayUrl('http://127.0.0.1:5293');
            });
        }
        if (this.dom.sidebarChipTunnel) {
            this.dom.sidebarChipTunnel.addEventListener('click', () => {
                this.detectAndApplyTunnel();
            });
        }

        // Viewport Scaling Modes
        const setScale = (mode, btn) => {
            [this.dom.scaleFit, this.dom.scaleStretch, this.dom.scaleOriginal].forEach(b => b?.classList.remove('active'));
            btn?.classList.add('active');
            this.streamClient.setScaleMode(mode);
            if (this.dom.labelScaleMode) {
                this.dom.labelScaleMode.textContent = mode === 'fit' ? 'Fit' : (mode === 'stretch' ? 'Stretch' : '1:1 Native');
            }
            if (this.dom.btnFloatScale) {
                this.dom.btnFloatScale.textContent = mode === 'fit' ? 'Fit' : (mode === 'stretch' ? 'Stretch' : '1:1');
            }
        };
        this.dom.scaleFit.addEventListener('click', () => setScale('fit', this.dom.scaleFit));
        this.dom.scaleStretch.addEventListener('click', () => setScale('stretch', this.dom.scaleStretch));
        this.dom.scaleOriginal.addEventListener('click', () => setScale('original', this.dom.scaleOriginal));

        if (this.dom.btnScaleToggle) {
            this.dom.btnScaleToggle.addEventListener('click', () => {
                const current = this.streamClient.scaleMode;
                if (current === 'fit') setScale('stretch', this.dom.scaleStretch);
                else if (current === 'stretch') setScale('original', this.dom.scaleOriginal);
                else setScale('fit', this.dom.scaleFit);
            });
        }

        // Floating Scale Mode Cycle
        this.dom.btnFloatScale.addEventListener('click', () => {
            const current = this.streamClient.scaleMode;
            if (current === 'fit') setScale('stretch', this.dom.scaleStretch);
            else if (current === 'stretch') setScale('original', this.dom.scaleOriginal);
            else setScale('fit', this.dom.scaleFit);
        });

        // Input Control Master Toggle
        const toggleInput = () => {
            const next = !this.inputHandler.inputEnabled;
            this.inputHandler.setInputEnabled(next);
            const label = next ? 'Control: ON' : 'Control: OFF';
            if (this.dom.labelToggleInput) {
                this.dom.labelToggleInput.textContent = label;
            } else {
                this.dom.btnToggleInput.textContent = label;
            }
            this.dom.btnFloatInput.textContent = label;
            this.dom.modalToggleInput.checked = next;
            if (this.dom.sidebarToggleInput) this.dom.sidebarToggleInput.checked = next;

            if (next) {
                this.dom.btnToggleInput.classList.remove('cyber-btn-outline');
                this.dom.btnToggleInput.classList.add('cyber-btn-primary');
                this.dom.btnFloatInput.classList.add('active');
            } else {
                this.dom.btnToggleInput.classList.remove('cyber-btn-primary');
                this.dom.btnToggleInput.classList.add('cyber-btn-outline');
                this.dom.btnFloatInput.classList.remove('active');
            }
            this.showToast(next ? 'Interactive input enabled' : 'View-only mode active', 'info');
        };
        this.dom.btnToggleInput.addEventListener('click', toggleInput);
        this.dom.btnFloatInput.addEventListener('click', toggleInput);
        this.dom.modalToggleInput.addEventListener('change', (e) => {
            if (this.inputHandler.inputEnabled !== e.target.checked) toggleInput();
        });
        if (this.dom.sidebarToggleInput) {
            this.dom.sidebarToggleInput.addEventListener('change', (e) => {
                if (this.inputHandler.inputEnabled !== e.target.checked) toggleInput();
            });
        }

        // Clipboard Sync Toggle
        if (this.dom.sidebarToggleClipboard) {
            this.dom.sidebarToggleClipboard.addEventListener('change', (e) => {
                this.clipboardSyncEnabled = e.target.checked;
                this.dom.modalToggleClipboard.checked = e.target.checked;
                this.showToast(`Clipboard sync ${e.target.checked ? 'enabled' : 'disabled'}`, 'info');
            });
        }
        this.dom.modalToggleClipboard.addEventListener('change', (e) => {
            this.clipboardSyncEnabled = e.target.checked;
            if (this.dom.sidebarToggleClipboard) this.dom.sidebarToggleClipboard.checked = e.target.checked;
        });

        // Stream Profiles
        this.dom.profileLan.addEventListener('click', () => {
            this.applyProfile(60, 65, 1080, 'Direct LAN (60 FPS, 1080p)');
            this.updateProfileButtons(this.dom.profileLan);
        });
        this.dom.profileWan.addEventListener('click', () => {
            this.applyProfile(40, 50, 720, 'WAN Turbo (40 FPS, 720p)');
            this.updateProfileButtons(this.dom.profileWan);
        });
        this.dom.profileSharp.addEventListener('click', () => {
            this.applyProfile(30, 85, 0, 'Ultra Sharp (30 FPS, Native)');
            this.updateProfileButtons(this.dom.profileSharp);
        });

        // Custom Stream Config Sliders (Toolbar Popover)
        const syncSliders = (fps, quality, maxHeight) => {
            this.dom.sliderFps.value = fps;
            this.dom.sliderFpsVal.textContent = fps;
            this.dom.modalSliderFps.value = fps;
            this.dom.modalValFps.textContent = `${fps} FPS`;

            this.dom.sliderQuality.value = quality;
            this.dom.sliderQualityVal.textContent = quality + '%';
            this.dom.modalSliderQuality.value = quality;
            this.dom.modalValQuality.textContent = quality + '%';

            this.dom.selectHeight.value = maxHeight.toString();
            this.dom.modalSelectRes.value = maxHeight.toString();

            this.streamClient.applyStreamConfig(fps, quality, maxHeight, this.dom.modalToggleLowLatency.checked ? 1 : 0);
            this.updateProfileButtons(null);
        };

        this.dom.sliderFps.addEventListener('input', (e) => syncSliders(parseInt(e.target.value, 10), parseInt(this.dom.sliderQuality.value, 10), parseInt(this.dom.selectHeight.value, 10)));
        this.dom.sliderQuality.addEventListener('input', (e) => syncSliders(parseInt(this.dom.sliderFps.value, 10), parseInt(e.target.value, 10), parseInt(this.dom.selectHeight.value, 10)));
        // Resolution Chips in Stream Tuning Popover
        const updateResChips = (res) => {
            const resInt = parseInt(res, 10);
            [this.dom.btnRes720, this.dom.btnRes1080, this.dom.btnResNative].forEach(b => b?.classList.remove('active'));
            if (resInt === 720) {
                this.dom.btnRes720?.classList.add('active');
                if (this.dom.valSliderResBadge) this.dom.valSliderResBadge.textContent = '720p HD';
            } else if (resInt === 1080) {
                this.dom.btnRes1080?.classList.add('active');
                if (this.dom.valSliderResBadge) this.dom.valSliderResBadge.textContent = '1080p FHD';
            } else {
                this.dom.btnResNative?.classList.add('active');
                if (this.dom.valSliderResBadge) this.dom.valSliderResBadge.textContent = 'Native 1:1';
            }
        };

        const setStreamResolution = (resInt) => {
            if (this.dom.selectHeight) this.dom.selectHeight.value = resInt.toString();
            if (this.dom.modalSelectRes) this.dom.modalSelectRes.value = resInt.toString();
            updateResChips(resInt);
            syncSliders(parseInt(this.dom.sliderFps.value, 10), parseInt(this.dom.sliderQuality.value, 10), resInt);
            this.showToast(`Stream max resolution set to ${resInt === 0 ? 'Native (Original)' : resInt + 'p'}`, 'info');
        };

        if (this.dom.btnRes720) this.dom.btnRes720.addEventListener('click', (e) => { e.stopPropagation(); setStreamResolution(720); });
        if (this.dom.btnRes1080) this.dom.btnRes1080.addEventListener('click', (e) => { e.stopPropagation(); setStreamResolution(1080); });
        if (this.dom.btnResNative) this.dom.btnResNative.addEventListener('click', (e) => { e.stopPropagation(); setStreamResolution(0); });

        this.dom.selectHeight.addEventListener('change', (e) => {
            const val = parseInt(e.target.value, 10);
            updateResChips(val);
            syncSliders(parseInt(this.dom.sliderFps.value, 10), parseInt(this.dom.sliderQuality.value, 10), val);
        });

        // Modal Settings Sliders
        this.dom.modalSliderFps.addEventListener('input', (e) => syncSliders(parseInt(e.target.value, 10), parseInt(this.dom.modalSliderQuality.value, 10), parseInt(this.dom.modalSelectRes.value, 10)));
        this.dom.modalSliderQuality.addEventListener('input', (e) => syncSliders(parseInt(this.dom.modalSliderFps.value, 10), parseInt(e.target.value, 10), parseInt(this.dom.modalSelectRes.value, 10)));
        this.dom.modalSelectRes.addEventListener('change', (e) => {
            const val = parseInt(e.target.value, 10);
            updateResChips(val);
            syncSliders(parseInt(this.dom.modalSliderFps.value, 10), parseInt(this.dom.modalSliderQuality.value, 10), val);
        });
        this.dom.modalToggleLowLatency.addEventListener('change', () => {
            syncSliders(parseInt(this.dom.sliderFps.value, 10), parseInt(this.dom.sliderQuality.value, 10), parseInt(this.dom.selectHeight.value, 10));
        });

        // Dropdown Toggles
        this.dom.screenSelectorBtn.addEventListener('click', (e) => { e.stopPropagation(); toggleMenu(this.dom.screenSelectorBtn, this.dom.screenSelectorMenu); });
        this.dom.quickActionsBtn.addEventListener('click', (e) => { e.stopPropagation(); toggleMenu(this.dom.quickActionsBtn, this.dom.quickActionsMenu); });
        this.dom.configBtn.addEventListener('click', (e) => { e.stopPropagation(); toggleMenu(this.dom.configBtn, this.dom.configMenu); });
        this.dom.btnFloatScreens.addEventListener('click', (e) => { e.stopPropagation(); toggleMenu(this.dom.btnFloatScreens, this.dom.screenSelectorMenu); });
        this.dom.btnFloatQuick.addEventListener('click', (e) => { e.stopPropagation(); toggleMenu(this.dom.btnFloatQuick, this.dom.quickActionsMenu); });

        // Dropdown Protection: Clicks inside dropdown menus or triggers must never dismiss them
        document.querySelectorAll('.dropdown-menu').forEach(menu => {
            menu.addEventListener('click', (e) => {
                // If it's an action item button in a quick menu (like quick keys injection or session jump)
                if (e.target.closest('.dropdown-item:not(.dropdown-item-no-close)')) {
                    setTimeout(() => menu.classList.remove('show'), 120);
                    return;
                }
                // Stop bubbling so window click listener doesn't dismiss form controls
                e.stopPropagation();
            });
            menu.addEventListener('pointerdown', (e) => e.stopPropagation());
        });

        // Close dropdowns only when clicking outside
        window.addEventListener('click', (e) => {
            if (e.target.closest('.dropdown-menu') || e.target.closest('.dropdown-wrapper') || e.target.closest('.header-more-container') || e.target.closest('.cyber-custom-select')) {
                return;
            }
            document.querySelectorAll('.dropdown-menu').forEach(m => m.classList.remove('show'));
            document.querySelectorAll('.cyber-select-menu').forEach(m => m.classList.remove('show'));
            document.querySelectorAll('.cyber-select-trigger').forEach(t => t.classList.remove('active'));
        });

        // Screen & Desktop Selection Clicks
        this.dom.screenSelectorMenu.addEventListener('click', (e) => {
            const item = e.target.closest('.dropdown-item');
            if (!item) return;

            this.dom.screenSelectorMenu.querySelectorAll('.dropdown-item').forEach(i => i.classList.remove('active'));
            item.classList.add('active');

            const dispAttr = item.getAttribute('data-display');
            const deskAttr = item.getAttribute('data-desktop');

            if (dispAttr !== null) {
                if (dispAttr === 'auto') {
                    this.selectedDisplayIndex = 0;
                    this.isAutoFollow = true;
                    this.dom.screenSelectorLabel.textContent = 'Screen: Auto ▾';
                } else {
                    this.selectedDisplayIndex = parseInt(dispAttr, 10);
                    this.isAutoFollow = false;
                    this.dom.screenSelectorLabel.textContent = `Screen: Display ${this.selectedDisplayIndex + 1} ▾`;
                }
            } else if (deskAttr !== null) {
                this.selectedDesktopName = deskAttr;
                this.dom.screenSelectorLabel.textContent = `Desktop: ${deskAttr} ▾`;
            }

            this.streamClient.sendSwitchDesktop(this.selectedDisplayIndex, this.isAutoFollow, this.selectedDesktopName);
            this.showToast(`Switched to ${this.dom.screenSelectorLabel.textContent.replace(' ▾', '')}`, 'info');
        });

        // Quick Action Shortcuts Binding
        document.querySelectorAll('.dropdown-item[data-action]').forEach(btn => {
            btn.addEventListener('click', () => {
                const action = btn.getAttribute('data-action');
                switch (action) {
                    case 'ctrl-alt-del': this.inputHandler.sendCtrlAltDel(); this.showToast('Sent Ctrl + Alt + Delete', 'info'); break;
                    case 'win-l': this.inputHandler.sendWinL(); this.showToast('Sent Lock (Win + L)', 'info'); break;
                    case 'win-r': this.inputHandler.sendWinR(); this.showToast('Sent Run (Win + R)', 'info'); break;
                    case 'taskmgr': this.inputHandler.sendTaskManager(); this.showToast('Sent Task Manager', 'info'); break;
                    case 'alt-tab': this.inputHandler.sendAltTab(); this.showToast('Sent Alt + Tab', 'info'); break;
                    case 'win': this.inputHandler.sendWinKey(); this.showToast('Sent Windows Key', 'info'); break;
                    case 'esc': this.inputHandler.sendEscKey(); this.showToast('Sent Escape', 'info'); break;
                }
            });
        });

        // Fullscreen Toggle + Keyboard Lock API
        const toggleFullscreen = async () => {
            const container = document.getElementById('app-container');
            if (!document.fullscreenElement) {
                try {
                    await container.requestFullscreen();
                    container.classList.add('is-fullscreen');
                    this.dom.floatingIsland.classList.remove('collapsed');
                    this.isIslandCollapsed = false;
                    this.startIslandAutoHide();

                    if ('keyboard' in navigator && typeof navigator.keyboard.lock === 'function') {
                        try {
                            await navigator.keyboard.lock(['Escape', 'AltLeft', 'AltRight', 'Tab', 'MetaLeft', 'MetaRight']);
                        } catch { }
                    }
                } catch { }
            } else {
                try {
                    await document.exitFullscreen();
                    container.classList.remove('is-fullscreen');
                    this.stopIslandAutoHide();
                    if ('keyboard' in navigator && typeof navigator.keyboard.unlock === 'function') {
                        navigator.keyboard.unlock();
                    }
                } catch { }
            }
        };

        this.dom.btnFullscreen.addEventListener('click', toggleFullscreen);
        this.dom.btnFloatExitFullscreen.addEventListener('click', toggleFullscreen);

        // Track fullscreen change (e.g. user pressed Esc on browser)
        document.addEventListener('fullscreenchange', () => {
            const container = document.getElementById('app-container');
            if (!document.fullscreenElement) {
                container.classList.remove('is-fullscreen');
                this.stopIslandAutoHide();
            } else {
                container.classList.add('is-fullscreen');
                this.startIslandAutoHide();
            }
        });

        // Floating Island Controls
        this.dom.btnFloatCollapse.addEventListener('click', () => {
            this.dom.floatingIsland.classList.add('collapsed');
            this.isIslandCollapsed = true;
            this.showToast('HUD collapsed. Move cursor to top to reveal.', 'info');
        });

        this.dom.floatEdgeTrigger.addEventListener('mouseenter', () => {
            this.dom.floatingIsland.classList.remove('collapsed');
            this.isIslandCollapsed = false;
            this.startIslandAutoHide();
        });

        this.dom.floatEdgeTrigger.addEventListener('click', () => {
            this.dom.floatingIsland.classList.remove('collapsed');
            this.isIslandCollapsed = false;
            this.startIslandAutoHide();
        });

        this.dom.btnFloatPin.addEventListener('click', () => {
            this.isIslandPinned = !this.isIslandPinned;
            this.dom.btnFloatPin.classList.toggle('active', this.isIslandPinned);
            this.dom.btnFloatPin.textContent = this.isIslandPinned ? '📌' : '📍';
            this.showToast(this.isIslandPinned ? 'HUD Pinned open' : 'HUD Auto-hide active', 'info');
            if (this.isIslandPinned) this.stopIslandAutoHide();
            else this.startIslandAutoHide();
        });

        this.dom.btnFloatDisconnect.addEventListener('click', () => this.stopSession());

        this.dom.btnOpenConnections.addEventListener('click', () => {
            openModal(this.dom.modalConnections);
            this.refreshConnectionsModal();
        });
        this.dom.btnPlaceholderConn.addEventListener('click', () => {
            openModal(this.dom.modalConnections);
            this.refreshConnectionsModal();
        });
        this.dom.btnPlaceholderEnroll.addEventListener('click', () => {
            openModal(this.dom.modalConnections);
            this.switchModalTab(this.dom.modalConnections, 'tab-enroll-new');
            this.generateEnrollmentCode();
        });
        this.dom.gatewayChip.addEventListener('click', () => {
            openModal(this.dom.modalConnections);
            this.switchModalTab(this.dom.modalConnections, 'tab-network-gateway');
        });

        this.dom.btnOpenSettings.addEventListener('click', () => {
            openModal(this.dom.modalSettings);
            this.refreshDiagnosticsModal();
        });
        this.dom.btnFloatSettings.addEventListener('click', () => {
            openModal(this.dom.modalSettings);
            this.refreshDiagnosticsModal();
        });

        this.dom.btnCloseConnections.addEventListener('click', closeModals);
        this.dom.btnCloseSettings.addEventListener('click', closeModals);
        this.dom.modalBackdrop.addEventListener('click', closeModals);

        // Escape Key Closes Modals
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.dom.modalBackdrop.classList.contains('show')) {
                closeModals();
            }
        });

        // Modal Tab Buttons
        document.querySelectorAll('.modal-tab-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const modal = btn.closest('.cyber-modal');
                const targetTab = btn.getAttribute('data-tab');
                this.switchModalTab(modal, targetTab);
            });
        });

        // Connections Modal Buttons
        this.dom.btnRefreshDevicesModal.addEventListener('click', () => this.refreshConnectionsModal());
        this.dom.btnRefreshCode.addEventListener('click', () => this.generateEnrollmentCode());
        this.dom.btnCopyCode.addEventListener('click', () => {
            navigator.clipboard.writeText(this.dom.wizardCode.textContent.trim());
            this.showToast('Copied invitation code to clipboard', 'success');
        });
        this.dom.btnCopyCmd.addEventListener('click', () => {
            navigator.clipboard.writeText(this.dom.wizardCmd.value.trim());
            this.showToast('Copied PowerShell command to clipboard', 'success');
        });

        // Gateway Switcher Chips
        this.dom.chipWifi.addEventListener('click', () => {
            const lanIp = this.dom.gatewayLanVal.textContent || '192.168.1.1';
            this.applyGatewayUrl(`http://${lanIp}:5293`);
        });
        this.dom.chipLocalhost.addEventListener('click', () => {
            this.applyGatewayUrl('http://127.0.0.1:5293');
        });
        this.dom.chipTunnel.addEventListener('click', () => {
            this.detectAndApplyTunnel();
        });
        this.dom.btnApplyGateway.addEventListener('click', () => {
            this.applyGatewayUrl(this.dom.inputCustomGateway.value.trim());
        });

        // Cloudflare Tunnel Launcher Actions
        if (this.dom.btnLaunchTunnelTerminal) {
            this.dom.btnLaunchTunnelTerminal.addEventListener('click', () => {
                this.launchTunnelTerminal();
            });
        }
        if (this.dom.btnSidebarLaunchTunnel) {
            this.dom.btnSidebarLaunchTunnel.addEventListener('click', () => {
                this.launchTunnelTerminal();
            });
        }
        if (this.dom.btnCopyTunnelCmd) {
            this.dom.btnCopyTunnelCmd.addEventListener('click', () => {
                this.copyTunnelCommand();
            });
        }
        if (this.dom.btnUseActiveTunnel) {
            this.dom.btnUseActiveTunnel.addEventListener('click', () => {
                const url = this.dom.tunnelActiveUrl ? this.dom.tunnelActiveUrl.textContent.trim() : '';
                if (url && url.startsWith('http')) {
                    this.applyGatewayUrl(url);
                    closeModals();
                    this.showToast(`Connected via Cloudflare Public Tunnel: ${url}`, 'success');
                }
            });
        }
        if (this.dom.btnApplyTunnelManual) {
            this.dom.btnApplyTunnelManual.addEventListener('click', () => {
                const url = this.dom.inputTunnelUrlManual ? this.dom.inputTunnelUrlManual.value.trim() : '';
                if (url && url.startsWith('http')) {
                    this.applyGatewayUrl(url);
                    closeModals();
                    this.showToast(`Applied Public Tunnel Gateway: ${url}`, 'success');
                } else {
                    this.showToast('Please enter a valid https://... URL', 'warn');
                }
            });
        }

        // Emergency Kill Button
        this.dom.btnEmergencyKill.addEventListener('click', async () => {
            if (!this.selectedDevice || !this.currentUser) {
                this.showToast('No active target device selected.', 'error');
                return;
            }
            if (confirm(`Emergency Disable Host: Are you sure you want to forcibly kill and revoke ${this.selectedDevice.hostname}?`)) {
                await this.emergencyKillHost(this.selectedDevice.deviceId);
            }
        });

        // Populate Operator Accounts in Settings
        this.renderAccountsDirectory();
    }

    startIslandAutoHide() {
        if (this.isIslandPinned) return;
        this.stopIslandAutoHide();
        this.islandAutoHideTimer = setTimeout(() => {
            if (!this.isIslandPinned && document.fullscreenElement) {
                this.dom.floatingIsland.classList.add('collapsed');
                this.isIslandCollapsed = true;
            }
        }, 3500);
    }

    stopIslandAutoHide() {
        if (this.islandAutoHideTimer) {
            clearTimeout(this.islandAutoHideTimer);
            this.islandAutoHideTimer = null;
        }
    }

    switchModalTab(modal, tabId) {
        modal.querySelectorAll('.modal-tab-btn').forEach(b => {
            b.classList.toggle('active', b.getAttribute('data-tab') === tabId);
        });
        modal.querySelectorAll('.modal-tab-pane').forEach(p => {
            p.classList.toggle('active', p.id === tabId);
        });
        if (tabId === 'tab-enroll-new') {
            this.generateEnrollmentCode();
        }
        if (tabId === 'tab-network-gateway') {
            this.refreshTunnelStatus();
        }
        if (tabId === 'tab-permanent-keys') {
            this.loadMasterKeys();
        }
    }

    updateProfileButtons(activeBtn) {
        [this.dom.profileLan, this.dom.profileWan, this.dom.profileSharp].forEach(b => b?.classList.remove('active'));
        if (activeBtn) activeBtn.classList.add('active');
    }

    applyProfile(fps, quality, maxHeight, label) {
        this.dom.sliderFps.value = fps;
        this.dom.sliderFpsVal.textContent = fps;
        this.dom.sliderQuality.value = quality;
        this.dom.sliderQualityVal.textContent = quality + '%';
        this.dom.selectHeight.value = maxHeight.toString();
        this.dom.modalSliderFps.value = fps;
        this.dom.modalValFps.textContent = `${fps} FPS`;
        this.dom.modalSliderQuality.value = quality;
        this.dom.modalValQuality.textContent = quality + '%';
        this.dom.modalSelectRes.value = maxHeight.toString();

        this.streamClient.applyStreamConfig(fps, quality, maxHeight, 1);
        this.showToast(`Applied ${label}`, 'info');
    }

    updateGatewayChip() {
        const url = this.serverUrl;
        let modeLabel = 'Mode: 💻 Localhost (Loopback)';
        if (url.includes('127.0.0.1') || url.includes('localhost')) {
            this.dom.gatewayChipText.textContent = '💻 Localhost:5293';
            modeLabel = 'Mode: 💻 Localhost (Loopback)';
            if (this.dom.sidebarChipLocalhost) {
                [this.dom.sidebarChipWifi, this.dom.sidebarChipLocalhost, this.dom.sidebarChipTunnel].forEach(c => c?.classList.remove('active'));
                this.dom.sidebarChipLocalhost.classList.add('active');
            }
        } else if (url.includes('trycloudflare.com') || url.includes('cloudflare')) {
            this.dom.gatewayChipText.textContent = '☁️ Cloudflare Tunnel';
            modeLabel = 'Mode: ☁️ Cloudflare Tunnel';
            if (this.dom.sidebarChipTunnel) {
                [this.dom.sidebarChipWifi, this.dom.sidebarChipLocalhost, this.dom.sidebarChipTunnel].forEach(c => c?.classList.remove('active'));
                this.dom.sidebarChipTunnel.classList.add('active');
            }
        } else {
            const host = url.replace(/https?:\/\//i, '').split(':')[0];
            this.dom.gatewayChipText.textContent = `📶 LAN (${host})`;
            modeLabel = `Mode: 📶 Local Wi-Fi (${host})`;
            if (this.dom.sidebarChipWifi) {
                [this.dom.sidebarChipWifi, this.dom.sidebarChipLocalhost, this.dom.sidebarChipTunnel].forEach(c => c?.classList.remove('active'));
                this.dom.sidebarChipWifi.classList.add('active');
            }
        }
        this.dom.inputCustomGateway.value = this.serverUrl;
        if (this.dom.sidebarTxtServer) this.dom.sidebarTxtServer.value = this.serverUrl;
        if (this.dom.sidebarGatewayModeBadge) this.dom.sidebarGatewayModeBadge.textContent = modeLabel;
    }

    applyGatewayUrl(newUrl) {
        if (!newUrl) return;
        this.serverUrl = newUrl.replace(/\/$/, '');
        localStorage.setItem('ER_GATEWAY_URL', this.serverUrl);
        this.updateGatewayChip();
        this.showToast(`Gateway set to ${this.serverUrl}. Reconnecting...`, 'success');
        this.authenticateUser(this.dom.userSelect.value);
    }

    async fetchGatewayApi(endpoint, options = {}) {
        const origins = [];
        if (typeof window !== 'undefined' && window.location && window.location.origin && window.location.origin.startsWith('http')) {
            origins.push(window.location.origin);
        }
        if (!origins.includes('http://127.0.0.1:5293')) origins.push('http://127.0.0.1:5293');
        if (!origins.includes('http://localhost:5293')) origins.push('http://localhost:5293');
        if (this.serverUrl && !origins.includes(this.serverUrl)) origins.push(this.serverUrl);

        for (const origin of origins) {
            try {
                const resp = await fetch(`${origin}${endpoint}`, options);
                if (resp && resp.ok) return resp;
            } catch { }
        }
        return null;
    }

    async detectAndApplyTunnel() {
        try {
            const resp = await this.fetchGatewayApi('/api/v1/gateway/tunnel');
            if (resp && resp.ok) {
                const data = await resp.json();
                if (data.active && data.url) {
                    this.applyGatewayUrl(data.url);
                    this.showToast(`Applied active Cloudflare Tunnel: ${data.url}`, 'success');
                    return;
                }
            }
        } catch { }
        this.showToast('No active Cloudflare tunnel detected. Launching tunnel terminal...', 'info');
        await this.launchTunnelTerminal();
    }

    async refreshTunnelStatus() {
        try {
            const resp = await this.fetchGatewayApi('/api/v1/gateway/tunnel');
            if (resp && resp.ok) {
                const data = await resp.json();
                if (data.active && data.url) {
                    if (this.dom.tunnelLiveBadge) {
                        this.dom.tunnelLiveBadge.textContent = 'ONLINE / READY';
                        this.dom.tunnelLiveBadge.className = 'tunnel-status-badge active';
                    }
                    if (this.dom.tunnelActiveRow) this.dom.tunnelActiveRow.style.display = 'flex';
                    if (this.dom.tunnelActiveUrl) this.dom.tunnelActiveUrl.textContent = data.url;
                    if (this.dom.inputTunnelUrlManual && !this.dom.inputTunnelUrlManual.value) {
                        this.dom.inputTunnelUrlManual.value = data.url;
                    }
                    return data;
                }
            }
        } catch { }

        if (this.dom.tunnelLiveBadge) {
            this.dom.tunnelLiveBadge.textContent = 'NOT RUNNING';
            this.dom.tunnelLiveBadge.className = 'tunnel-status-badge inactive';
        }
        if (this.dom.tunnelActiveRow) this.dom.tunnelActiveRow.style.display = 'none';
        return { active: false };
    }

    async launchTunnelTerminal() {
        try {
            this.showToast('Launching Cloudflare tunnel in a new terminal window...', 'info');
            const resp = await this.fetchGatewayApi('/api/v1/gateway/start-tunnel', { method: 'POST' });
            if (!resp || !resp.ok) throw new Error('Could not reach Gateway host API');
            const data = await resp.json();
            this.showToast(data.message || 'Tunnel terminal launched! Awaiting public URL...', 'success');

            // Open connections modal to gateway tab
            this.dom.modalBackdrop?.classList.add('show');
            this.dom.modalConnections?.classList.add('show');
            this.switchModalTab(this.dom.modalConnections, 'tab-network-gateway');

            // Poll for tunnel URL over next 30 seconds
            let attempts = 0;
            const pollInterval = setInterval(async () => {
                attempts++;
                const status = await this.refreshTunnelStatus();
                if (status && status.active && status.url) {
                    clearInterval(pollInterval);
                    this.applyGatewayUrl(status.url);
                    this.showToast(`Cloudflare Tunnel is active and connected! URL: ${status.url}`, 'success');
                } else if (attempts > 15) {
                    clearInterval(pollInterval);
                }
            }, 2000);
        } catch (err) {
            this.showToast(`Failed to launch tunnel terminal: ${err.message}`, 'error');
        }
    }

    copyTunnelCommand() {
        const cmd = 'powershell -ExecutionPolicy Bypass -File .\\scripts\\start_tunnel.ps1';
        navigator.clipboard.writeText(cmd);
        this.showToast('Copied tunnel script command to clipboard!', 'success');
    }

    async initAuthAndDevices() {
        const defaultEmail = this.dom.userSelect.value || 'admin@corp.internal';
        await this.authenticateUser(defaultEmail);

        // Auto-discover newly enrolled devices every 3 seconds while not in an active session
        setInterval(() => {
            if (!this.activeSession && this.currentUser) {
                this.refreshDeviceList();
            }
        }, 3000);
    }

    async authenticateUser(email) {
        try {
            const resp = await fetch(`${this.serverUrl}/api/v1/auth/token`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email })
            });

            if (!resp.ok) throw new Error(`Auth failed: HTTP ${resp.status}`);
            this.currentUser = await resp.json();
            this.dom.gatewayStatusVal.textContent = 'Online • Authenticated';
            this.dom.gatewayStatusVal.className = 'stat-val good';
            await this.refreshDeviceList();
        } catch (err) {
            this.dom.gatewayStatusVal.textContent = `Offline • ${err.message}`;
            this.dom.gatewayStatusVal.className = 'stat-val warn';
            this.showToast(`Gateway error: ${err.message}`, 'error');
            if (typeof window !== 'undefined' && window.location && window.location.origin && this.serverUrl !== window.location.origin && window.location.origin.startsWith('http')) {
                console.warn(`Gateway ${this.serverUrl} unreachable. Falling back to local origin: ${window.location.origin}`);
                this.serverUrl = window.location.origin;
                localStorage.setItem('ER_GATEWAY_URL', this.serverUrl);
                this.updateGatewayChip();
                this.authenticateUser(email);
            }
        }
    }

    syncKeyForSelectedDevice() {
        if (!this.selectedDevice) {
            const fallbackKey = localStorage.getItem('erp_active_master_key') || localStorage.getItem('erp_saved_key') || '';
            this.dom.keyInput.value = fallbackKey;
            if (this.dom.sidebarTxtKey) this.dom.sidebarTxtKey.value = fallbackKey;
            this.updateHeaderTargetDisplay();
            return;
        }
        const devId = this.selectedDevice.deviceId;
        let token = '';
        const savedMasterKey = localStorage.getItem('erp_active_master_key');
        if (savedMasterKey) {
            token = savedMasterKey;
        } else if (this.permanentTokens[devId]) {
            token = this.permanentTokens[devId];
        } else if (localStorage.getItem('erp_saved_key')) {
            token = localStorage.getItem('erp_saved_key');
        } else if (this.selectedDevice.shortPin) {
            token = this.selectedDevice.shortPin;
        } else if (this.selectedDevice.permanentAccessToken) {
            token = this.selectedDevice.permanentAccessToken;
        }
        this.dom.keyInput.value = token;
        if (this.dom.sidebarTxtKey) this.dom.sidebarTxtKey.value = token;
        this.updateHeaderTargetDisplay();
    }

    updateHeaderTargetDisplay() {
        if (this.selectedDevice) {
            const name = (this.selectedDevice.hostname || this.selectedDevice.deviceId || 'Remote PC').trim();
            if (this.dom.headerTargetName) this.dom.headerTargetName.textContent = `Target: ${name}`;
            if (this.dom.headerTargetChip) {
                this.dom.headerTargetChip.classList.add('has-target');
                this.dom.headerTargetChip.title = `Active Target Machine: ${name} (${this.selectedDevice.deviceId || ''})`;
            }
            if (this.dom.labelTriggerDevice) {
                this.dom.labelTriggerDevice.textContent = name;
            }
            if (this.dom.badgeTriggerDevice) {
                const isOnline = this.selectedDevice.status === 'Online' || this.selectedDevice.status === 0;
                this.dom.badgeTriggerDevice.textContent = isOnline ? 'ONLINE' : 'OFFLINE';
                this.dom.badgeTriggerDevice.className = `trigger-badge ${isOnline ? 'good' : 'warn'}`;
            }
        } else {
            if (this.dom.headerTargetName) this.dom.headerTargetName.textContent = 'Target: None';
            if (this.dom.headerTargetChip) {
                this.dom.headerTargetChip.classList.remove('has-target');
                this.dom.headerTargetChip.title = 'No target machine selected';
            }
            if (this.dom.labelTriggerDevice) {
                this.dom.labelTriggerDevice.textContent = 'Select Target PC';
            }
            if (this.dom.badgeTriggerDevice) {
                this.dom.badgeTriggerDevice.textContent = 'NONE';
                this.dom.badgeTriggerDevice.className = 'trigger-badge';
            }
        }
    }

    async refreshDeviceList() {
        if (!this.currentUser) return;
        try {
            const resp = await fetch(`${this.serverUrl}/api/v1/devices?userId=${encodeURIComponent(this.currentUser.userId)}`);
            if (!resp.ok) throw new Error(`Device list failed: HTTP ${resp.status}`);

            const allDevices = await resp.json();
            
            // Deduplicate by hostname (preferring status === 'Online' or status === 0)
            const deviceMap = new Map();
            allDevices.forEach(d => {
                const key = (d.hostname || d.deviceId || '').trim().toLowerCase();
                if (!deviceMap.has(key)) {
                    deviceMap.set(key, d);
                } else {
                    const existing = deviceMap.get(key);
                    const isNewer = (d.status === 'Online' || d.status === 0) && (existing.status !== 'Online' && existing.status !== 0);
                    if (isNewer) {
                        deviceMap.set(key, d);
                    }
                }
            });

            this.devices = Array.from(deviceMap.values());
            const previousSelectedId = this.dom.deviceSelect.value;
            this.dom.deviceSelect.innerHTML = '';
            if (this.dom.sidebarSelectDevice) this.dom.sidebarSelectDevice.innerHTML = '';

            if (this.devices.length === 0) {
                const opt1 = document.createElement('option');
                opt1.value = '';
                opt1.textContent = 'No enrolled devices found';
                this.dom.deviceSelect.appendChild(opt1);

                if (this.dom.sidebarSelectDevice) {
                    const opt2 = document.createElement('option');
                    opt2.value = '';
                    opt2.textContent = 'No enrolled devices found';
                    this.dom.sidebarSelectDevice.appendChild(opt2);
                }

                if (this.dom.menuCustomDevice) {
                    this.dom.menuCustomDevice.innerHTML = '<div class="dropdown-item-empty">No enrolled devices found</div>';
                }

                this.selectedDevice = null;
                this.updateHeaderTargetDisplay();
                this.dom.btnConnect.disabled = true;
                if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = true;
                return;
            }

            const onlineDevices = this.devices.filter(d => d.status === 'Online' || d.status === 0);
            const offlineDevices = this.devices.filter(d => d.status !== 'Online' && d.status !== 0);

            const populateSelect = (selectEl) => {
                if (!selectEl) return;
                selectEl.innerHTML = '';
                if (onlineDevices.length > 0) {
                    const grpOnline = document.createElement('optgroup');
                    grpOnline.label = '🟢 LIVE / ONLINE NOW';
                    onlineDevices.forEach(dev => {
                        const opt = document.createElement('option');
                        opt.value = dev.deviceId;
                        const pin = dev.shortPin ? ` [PIN: ${dev.shortPin}]` : '';
                        opt.textContent = `🟢 ${dev.hostname} (${dev.deviceId})${pin} — Live Online`;
                        grpOnline.appendChild(opt);
                    });
                    selectEl.appendChild(grpOnline);
                }

                if (offlineDevices.length > 0) {
                    const grpOffline = document.createElement('optgroup');
                    grpOffline.label = '⚪ PAST / OFFLINE SESSIONS';
                    offlineDevices.forEach(dev => {
                        const opt = document.createElement('option');
                        opt.value = dev.deviceId;
                        opt.textContent = `⚪ ${dev.hostname} (${dev.deviceId}) — Offline`;
                        grpOffline.appendChild(opt);
                    });
                    selectEl.appendChild(grpOffline);
                }
            };

            populateSelect(this.dom.deviceSelect);
            populateSelect(this.dom.sidebarSelectDevice);

            // Populate custom cyber dropdown in three-dot menu
            if (this.dom.menuCustomDevice) {
                this.dom.menuCustomDevice.innerHTML = '';
                if (onlineDevices.length > 0) {
                    const sec = document.createElement('div');
                    sec.className = 'dropdown-section-title';
                    sec.textContent = '🟢 LIVE / ONLINE NOW';
                    this.dom.menuCustomDevice.appendChild(sec);
                    onlineDevices.forEach(dev => {
                        const btn = document.createElement('button');
                        btn.className = 'dropdown-item';
                        btn.setAttribute('data-device-id', dev.deviceId);
                        const pin = dev.shortPin ? ` [PIN: ${dev.shortPin}]` : '';
                        btn.innerHTML = `<span class="item-text">🟢 ${dev.hostname}${pin}</span><span class="item-badge live">ONLINE</span>`;
                        this.dom.menuCustomDevice.appendChild(btn);
                    });
                }
                if (offlineDevices.length > 0) {
                    const sec = document.createElement('div');
                    sec.className = 'dropdown-section-title';
                    sec.textContent = '⚪ PAST / OFFLINE SESSIONS';
                    this.dom.menuCustomDevice.appendChild(sec);
                    offlineDevices.forEach(dev => {
                        const btn = document.createElement('button');
                        btn.className = 'dropdown-item';
                        btn.setAttribute('data-device-id', dev.deviceId);
                        btn.innerHTML = `<span class="item-text">⚪ ${dev.hostname}</span><span class="item-badge">OFFLINE</span>`;
                        this.dom.menuCustomDevice.appendChild(btn);
                    });
                }
            }

            // Restore selection from previous state, persistent storage, or pick first online device
            const savedDevicePref = localStorage.getItem('erp_last_selected_device');
            let targetId = '';
            if (previousSelectedId && this.devices.some(d => d.deviceId === previousSelectedId)) {
                targetId = previousSelectedId;
            } else if (savedDevicePref && this.devices.some(d => d.deviceId === savedDevicePref)) {
                targetId = savedDevicePref;
            } else if (onlineDevices.length > 0) {
                targetId = onlineDevices[0].deviceId;
            } else {
                targetId = this.devices[0].deviceId;
            }

            this.dom.deviceSelect.value = targetId;
            if (this.dom.sidebarSelectDevice) this.dom.sidebarSelectDevice.value = targetId;
            this.selectedDevice = this.devices.find(d => d.deviceId === targetId) || null;
            if (this.selectedDevice) {
                localStorage.setItem('erp_last_selected_device', this.selectedDevice.deviceId);
            }

            if (this.dom.menuCustomDevice) {
                this.dom.menuCustomDevice.querySelectorAll('.dropdown-item').forEach(b => {
                    b.classList.toggle('active', b.getAttribute('data-device-id') === targetId);
                });
            }

            this.syncKeyForSelectedDevice();
            this.updateHeaderTargetDisplay();
            this.dom.btnConnect.disabled = false;
            if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = false;
        } catch (err) {
            // Error handling
        }
    }

    async refreshConnectionsModal() {
        await this.refreshDeviceList();
        const container = this.dom.connectionsGrid;
        container.innerHTML = '';

        if (!this.devices || this.devices.length === 0) {
            container.innerHTML = '<div class="device-empty-state">No workstations enrolled yet. Click "+ Enroll Remote PC" to add one.</div>';
            return;
        }

        this.devices.forEach(dev => {
            const isOnline = dev.status === 'Online' || dev.status === 0;
            const card = document.createElement('div');
            card.className = 'endpoint-card';

            const savedKey = this.permanentTokens[dev.deviceId] || dev.shortPin || dev.permanentAccessToken || 'None';

            card.innerHTML = `
                <div class="endpoint-header">
                    <div class="endpoint-title-row">
                        <span class="cyber-badge-dot" style="background:${isOnline ? 'var(--status-success)' : 'var(--status-neutral)'}"></span>
                        <span class="endpoint-hostname">${dev.hostname || 'Remote PC'}</span>
                    </div>
                    <span class="cyber-badge ${isOnline ? 'badge-connected' : 'badge-idle'}">
                        ${isOnline ? 'ONLINE' : 'OFFLINE'}
                    </span>
                </div>
                <div class="endpoint-meta">
                    <div><strong>ID:</strong> ${dev.deviceId}</div>
                    <div><strong>OS:</strong> ${dev.osVersion || 'Windows 11 Enterprise'}</div>
                    <div><strong>Group:</strong> ${dev.groupId || 'GRP-CORP-DESKTOPS'}</div>
                    <div><strong>Key/PIN:</strong> <code>${savedKey}</code></div>
                </div>
                <div class="endpoint-actions">
                    <button class="cyber-btn cyber-btn-primary btn-card-connect" data-dev="${dev.deviceId}">
                        ${isOnline ? 'Connect Now' : 'Connect (Wake)'}
                    </button>
                    <button class="cyber-btn cyber-btn-outline btn-card-copy-token" data-token="${savedKey}">
                        Copy Token
                    </button>
                </div>
            `;

            card.querySelector('.btn-card-connect').addEventListener('click', () => {
                this.dom.deviceSelect.value = dev.deviceId;
                if (this.dom.sidebarSelectDevice) this.dom.sidebarSelectDevice.value = dev.deviceId;
                this.selectedDevice = dev;
                this.syncKeyForSelectedDevice();
                this.dom.modalBackdrop.classList.remove('show');
                this.dom.modalConnections.classList.remove('show');
                this.startSession();
            });

            card.querySelector('.btn-card-copy-token').addEventListener('click', () => {
                navigator.clipboard.writeText(savedKey);
                this.showToast('Copied device access token', 'success');
            });

            container.appendChild(card);
        });
    }

    escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    async loadMasterKeys() {
        if (!this.dom.masterKeysContainer) return;
        try {
            this.dom.masterKeysContainer.innerHTML = '<div class="device-empty-state">Loading permanent master keys...</div>';
            const resp = await fetch(`${this.serverUrl}/api/v1/master-keys?userId=${encodeURIComponent(this.currentUser?.userId || '')}`);
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            const keys = await resp.json();
            this.renderMasterKeys(keys);
        } catch (err) {
            console.error('Failed to load master keys:', err);
            this.dom.masterKeysContainer.innerHTML = `<div class="device-empty-state" style="color:var(--status-danger)">Error loading master keys: ${err.message}</div>`;
        }
    }

    renderMasterKeys(keys) {
        if (!this.dom.masterKeysContainer) return;
        if (!keys || keys.length === 0) {
            this.dom.masterKeysContainer.innerHTML = '<div class="device-empty-state">No permanent master keys configured yet. Create one above to enable universal unattended access across all machines.</div>';
            return;
        }

        const activePermanentKey = (this.dom.keyInput?.value || this.dom.sidebarTxtKey?.value || '').trim();

        this.dom.masterKeysContainer.innerHTML = '';
        keys.forEach(k => {
            const isCurrentlySelected = activePermanentKey && activePermanentKey.toLowerCase() === k.secretValue.trim().toLowerCase();
            const card = document.createElement('div');
            card.className = `master-key-card ${isCurrentlySelected ? 'active-key-border' : ''}`;

            const createdDate = k.createdAtUtc ? new Date(k.createdAtUtc).toLocaleDateString() : 'Permanent';
            const statusBadge = k.isActive 
                ? '<span class="status-badge online">ACTIVE</span>' 
                : '<span class="status-badge offline">DISABLED</span>';

            card.innerHTML = `
                <div class="master-key-top-row">
                    <div class="master-key-title-group">
                        <span class="master-key-title">🔑 ${this.escapeHtml(k.name || 'Master Access Key')}</span>
                        ${statusBadge}
                        <span class="info-badge" style="font-size:8px;">ALL DEVICES</span>
                        ${isCurrentlySelected ? '<span class="status-badge active" style="font-size:9px;">CURRENTLY IN USE</span>' : ''}
                    </div>
                    <div class="master-key-meta">
                        <span>Created: ${createdDate}</span>
                        <span>Scope: Universal Unattended</span>
                    </div>
                </div>

                <div class="master-key-secret-box">
                    <span class="master-key-secret-value masked" data-secret="${this.escapeHtml(k.secretValue)}">••••••••••••••••</span>
                    <button class="icon-toggle-btn btn-toggle-secret" title="Show/Hide Key Value" type="button">👁</button>
                    <button class="cyber-btn cyber-btn-outline btn-copy-secret" title="Copy Key to Clipboard" type="button" style="padding: 2px 8px; font-size: 11px;">📋 Copy</button>
                </div>

                ${k.description ? `<div style="font-size: 11px; color: var(--text-secondary);">${this.escapeHtml(k.description)}</div>` : ''}

                <div class="master-key-actions">
                    <button class="cyber-btn cyber-btn-primary btn-apply-master-key" type="button" style="padding: 4px 10px; font-size: 11px;">
                        <span>⚡ Use This Key for All Machines</span>
                    </button>
                    <button class="cyber-btn cyber-btn-outline btn-toggle-active-key" type="button" style="padding: 4px 8px; font-size: 11px;">
                        <span>${k.isActive ? 'Disable' : 'Enable'}</span>
                    </button>
                    <button class="cyber-btn cyber-btn-danger btn-delete-master-key" type="button" style="padding: 4px 8px; font-size: 11px;" title="Revoke and delete this key">
                        <span>🗑 Delete</span>
                    </button>
                </div>
            `;

            // Wire up actions
            const secretSpan = card.querySelector('.master-key-secret-value');
            const toggleSecretBtn = card.querySelector('.btn-toggle-secret');
            let isRevealed = false;
            toggleSecretBtn.addEventListener('click', () => {
                isRevealed = !isRevealed;
                secretSpan.textContent = isRevealed ? k.secretValue : '••••••••••••••••';
            });

            card.querySelector('.btn-copy-secret').addEventListener('click', () => {
                navigator.clipboard.writeText(k.secretValue);
                this.showToast(`Master key copied: ${k.secretValue}`, 'success');
            });

            card.querySelector('.btn-apply-master-key').addEventListener('click', () => {
                this.applyUniversalMasterKey(k.secretValue, k.name);
            });

            card.querySelector('.btn-toggle-active-key').addEventListener('click', async () => {
                await this.toggleMasterKey(k.keyId);
            });

            card.querySelector('.btn-delete-master-key').addEventListener('click', async () => {
                if (confirm(`Are you sure you want to revoke and delete master key "${k.name}"? Any sessions using it will lose unattended access.`)) {
                    await this.deleteMasterKey(k.keyId);
                }
            });

            this.dom.masterKeysContainer.appendChild(card);
        });
    }

    applyUniversalMasterKey(secretValue, keyName) {
        if (!secretValue) return;
        const trimmed = secretValue.trim();
        if (this.dom.keyInput) {
            this.dom.keyInput.value = trimmed;
        }
        if (this.dom.sidebarTxtKey) {
            this.dom.sidebarTxtKey.value = trimmed;
        }
        localStorage.setItem('erp_active_master_key', trimmed);

        if (this.selectedDevice) {
            this.savePermanentToken(this.selectedDevice.deviceId, trimmed);
        }

        this.showToast(`Active Master Key applied: ${keyName || trimmed} (works for ALL devices)`, 'success');
        this.dom.modalBackdrop?.classList.remove('show');
        this.dom.modalConnections?.classList.remove('show');
    }

    async saveMasterKey() {
        const name = (this.dom.txtMasterKeyName?.value || '').trim() || 'Universal Master Key';
        let secret = (this.dom.txtMasterKeyVal?.value || '').trim();
        const desc = (this.dom.txtMasterKeyDesc?.value || '').trim();

        if (!secret) {
            secret = `PERM-MASTER-${Math.random().toString(36).substring(2, 10).toUpperCase()}`;
            if (this.dom.txtMasterKeyVal) this.dom.txtMasterKeyVal.value = secret;
        }

        try {
            const resp = await fetch(`${this.serverUrl}/api/v1/master-keys`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    secretValue: secret,
                    description: desc,
                    createdBy: this.currentUser?.email || 'admin@corp.internal',
                    orgId: this.currentUser?.orgId || 'ORG-ENTERPRISE',
                    isActive: true
                })
            });

            if (!resp.ok) {
                const errData = await resp.json().catch(() => ({}));
                throw new Error(errData.error || `HTTP ${resp.status}`);
            }

            this.showToast(`Permanent Master Key "${name}" saved and activated!`, 'success');
            if (this.dom.txtMasterKeyVal) this.dom.txtMasterKeyVal.value = '';
            if (this.dom.txtMasterKeyDesc) this.dom.txtMasterKeyDesc.value = '';
            await this.loadMasterKeys();
        } catch (err) {
            console.error('Failed to save master key:', err);
            this.showToast(`Error saving master key: ${err.message}`, 'error');
        }
    }

    async toggleMasterKey(keyId) {
        try {
            const resp = await fetch(`${this.serverUrl}/api/v1/master-keys/${encodeURIComponent(keyId)}/toggle`, {
                method: 'PATCH'
            });
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            this.showToast('Master key status updated', 'info');
            await this.loadMasterKeys();
        } catch (err) {
            console.error('Failed to toggle master key:', err);
            this.showToast(`Error toggling master key: ${err.message}`, 'error');
        }
    }

    async deleteMasterKey(keyId) {
        try {
            const resp = await fetch(`${this.serverUrl}/api/v1/master-keys/${encodeURIComponent(keyId)}`, {
                method: 'DELETE'
            });
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            this.showToast('Permanent master key revoked and deleted', 'info');
            await this.loadMasterKeys();
        } catch (err) {
            console.error('Failed to delete master key:', err);
            this.showToast(`Error deleting master key: ${err.message}`, 'error');
        }
    }

    async generateEnrollmentCode() {
        this.dom.wizardCode.textContent = 'Generating...';
        try {
            const resp = await fetch(`${this.serverUrl}/api/v1/invitations/create`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    AdminUserId: this.currentUser ? this.currentUser.userId : 'USR-ADMIN-01',
                    GroupId: 'GRP-CORP-DESKTOPS',
                    TtlMinutes: 60,
                    AttendedRequired: false
                })
            });

            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            const data = await resp.json();

            const invCode = data.invitationCode || data.plainCode || data.PlainCode || data.invitationId || data.InvitationId || 'INV-AUTO-PAIR';
            this.dom.wizardCode.textContent = invCode;
            this.dom.wizardPin.textContent = data.shortPin || data.ShortPin || (invCode.includes('-') ? invCode.split('-').pop() : '1234');

            const cmd = `powershell -ExecutionPolicy Bypass -Command "& .\\EnterpriseRemote.Host.Service.exe --enroll ${invCode} --server ${this.serverUrl}"`;
            this.dom.wizardCmd.value = cmd;
        } catch (err) {
            this.dom.wizardCode.textContent = 'INV-AUTO-PAIR';
            this.dom.wizardPin.textContent = '1234';
            this.dom.wizardCmd.value = `.\\EnterpriseRemote.Host.Service.exe --enroll 1234 --server ${this.serverUrl}`;
        }
    }

    renderAccountsDirectory() {
        const accounts = [
            { email: 'admin@corp.internal', name: 'Security Admin', role: 'Enterprise Administrator' },
            { email: 'admin', name: 'System Admin', role: 'Local Quick Admin' },
            { email: 'sujal@corp.internal', name: 'Sujal (Admin)', role: 'Root Operator' },
            { email: 'sujal', name: 'Sujal', role: 'Admin Operator' },
            { email: 'alice@corp.internal', name: 'Alice Admin', role: 'Endpoint Manager' },
            { email: 'operator@corp.internal', name: 'Remote Operator', role: 'Session Operator' }
        ];

        const container = this.dom.accountsCardsList;
        container.innerHTML = '';

        accounts.forEach(acc => {
            const card = document.createElement('div');
            card.className = `account-card ${this.currentUser && this.currentUser.email === acc.email ? 'active' : ''}`;
            card.innerHTML = `
                <strong>${acc.name}</strong>
                <span>${acc.email}</span>
                <p style="font-size:10px; color:var(--text-secondary); margin-top:4px;">${acc.role}</p>
            `;
            card.addEventListener('click', () => {
                this.dom.userSelect.value = acc.email;
                if (this.dom.sidebarSelectUser) this.dom.sidebarSelectUser.value = acc.email;
                this.authenticateUser(acc.email);
                this.showToast(`Switched operator identity to ${acc.name}`, 'success');
                this.renderAccountsDirectory();
            });
            container.appendChild(card);
        });
    }

    refreshDiagnosticsModal() {
        this.dom.diagHostname.textContent = this.selectedDevice ? this.selectedDevice.hostname : 'None';
        this.dom.diagDeviceId.textContent = this.selectedDevice ? this.selectedDevice.deviceId : 'None';
        this.dom.diagSessionId.textContent = this.activeSession ? this.activeSession.sessionId : 'No Active Session';
        this.dom.diagGatewayUrl.textContent = this.serverUrl;
        this.dom.diagRtt.textContent = `${this.streamClient.rttMs} ms`;
        this.dom.diagFrames.textContent = this.streamClient.frameCount.toString();
        this.dom.diagBytes.textContent = `${Math.round(this.streamClient.bytesReceived / 1024)} KB`;
    }

    async emergencyKillHost(deviceId) {
        try {
            const adminId = this.currentUser ? this.currentUser.userId : 'USR-ADMIN-01';
            const resp = await fetch(`${this.serverUrl}/api/v1/devices/${encodeURIComponent(deviceId)}/emergency-disable?adminUserId=${encodeURIComponent(adminId)}`, {
                method: 'POST'
            });
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            this.showToast(`Device ${deviceId} emergency disabled.`, 'error');
            this.stopSession();
            await this.refreshDeviceList();
        } catch (err) {
            this.showToast(`Kill switch failed: ${err.message}`, 'error');
        }
    }

    onDesktopListUpdate(list) {
        if (!list) return;
        if (list.displays && list.displays.length > 0) {
            this.availableDisplays = list.displays;
        }
        if (list.desktops && list.desktops.length > 0) {
            this.availableDesktops = list.desktops;
        }
    }

    initClipboardListener() {
        window.addEventListener('focus', async () => {
            if (!this.clipboardSyncEnabled || !this.activeSession) return;
            try {
                if (navigator.clipboard && navigator.clipboard.readText) {
                    const text = await navigator.clipboard.readText();
                    if (text && text !== this.lastClipboardText) {
                        this.lastClipboardText = text;
                    }
                }
            } catch { }
        });
    }

    async startSession() {
        if (!this.selectedDevice || !this.currentUser) return;

        this.dom.btnConnect.disabled = true;
        if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = true;
        this.setBadge('connecting', 'Authorizing Session...');

        try {
            const enteredKey = this.dom.keyInput.value.trim() || 
                               (this.dom.sidebarTxtKey ? this.dom.sidebarTxtKey.value.trim() : '') ||
                               localStorage.getItem('erp_active_master_key') ||
                               localStorage.getItem('erp_saved_key') ||
                               this.selectedDevice.permanentAccessToken || 
                               null;
            if (enteredKey) {
                localStorage.setItem('erp_saved_key', enteredKey);
                this.savePermanentToken(this.selectedDevice.deviceId, enteredKey);
            }

            const sessionPayload = {
                operatorId: this.currentUser.userId,
                deviceId: this.selectedDevice.deviceId,
                requestedCapabilities: 7, // Screen(1) | Input(2) | Clipboard(4)
                permanentAccessToken: enteredKey
            };

            const resp = await fetch(`${this.serverUrl}/api/v1/sessions/request`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(sessionPayload)
            });

            if (!resp.ok) {
                const errData = await resp.json().catch(() => ({}));
                throw new Error(errData.error || `HTTP ${resp.status}`);
            }

            this.activeSession = await resp.json();

            // Connect WebSocket stream
            this.streamClient.connect(this.serverUrl, this.selectedDevice.deviceId, this.activeSession.sessionId);

            // Auto-detect tunnel or direct LAN
            const isTunnel = window.location.hostname.includes('trycloudflare.com') ||
                             window.location.hostname.includes('cloudflare');
            if (isTunnel) {
                this.applyProfile(40, 50, 720, 'WAN Turbo Profile');
                this.updateProfileButtons(this.dom.profileWan);
            } else {
                this.applyProfile(60, 65, 1080, 'Direct LAN Profile');
                this.updateProfileButtons(this.dom.profileLan);
            }

            this.dom.btnDisconnect.disabled = false;
            if (this.dom.btnSidebarDisconnect) this.dom.btnSidebarDisconnect.disabled = false;
            this.dom.placeholder.style.display = 'none';
            this.dom.floatDeviceName.textContent = this.selectedDevice.hostname || 'Host';

            if (this.dom.sidebarHudTarget) this.dom.sidebarHudTarget.textContent = this.selectedDevice.hostname || 'Host';
            if (this.dom.sidebarHudStatus) {
                this.dom.sidebarHudStatus.textContent = 'Streaming Live';
                this.dom.sidebarHudStatus.className = 'hud-good';
            }

            this.showToast(`Session established with ${this.selectedDevice.hostname}`, 'success');
        } catch (err) {
            this.setBadge('idle', 'Connection Failed');
            this.dom.btnConnect.disabled = false;
            if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = false;
            this.showToast(`Connection error: ${err.message}`, 'error');
        }
    }

    async autoReconnectSession() {
        if (!this.selectedDevice || !this.currentUser || this.streamClient.userDisconnected) return;

        this.setBadge('connecting', 'Auto-Reconnecting...');
        this.showToast(`Auto-reconnecting to ${this.selectedDevice.hostname}...`, 'info');

        try {
            const enteredKey = (this.dom.keyInput?.value || this.dom.sidebarTxtKey?.value || '').trim() ||
                               localStorage.getItem('erp_active_master_key') ||
                               localStorage.getItem('erp_saved_key') ||
                               this.selectedDevice.permanentAccessToken ||
                               null;

            const sessionPayload = {
                operatorId: this.currentUser.userId,
                deviceId: this.selectedDevice.deviceId,
                requestedCapabilities: 7, // Screen(1) | Input(2) | Clipboard(4)
                permanentAccessToken: enteredKey
            };

            const resp = await fetch(`${this.serverUrl}/api/v1/sessions/request`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(sessionPayload)
            });

            if (!resp.ok) {
                throw new Error(`HTTP ${resp.status}`);
            }

            this.activeSession = await resp.json();
            this.streamClient.connect(this.serverUrl, this.selectedDevice.deviceId, this.activeSession.sessionId);
            this.setBadge('connected', 'Live Stream Reconnected');
            this.showToast(`Reconnected to ${this.selectedDevice.hostname}!`, 'success');
        } catch (err) {
            console.warn('[Auto-Reconnect] Session refresh failed:', err);
            this.setBadge('reconnecting', 'Auto-reconnect failed. Retrying in 2s...');
            setTimeout(() => {
                if (!this.streamClient.userDisconnected) {
                    this.autoReconnectSession();
                }
            }, 2500);
        }
    }

    initNetworkListeners() {
        window.addEventListener('online', async () => {
            this.showToast('Network online (WiFi connected). Auto-reconnecting...', 'info');
            if (this.selectedDevice && (!this.streamClient.isConnected || this.streamClient.reconnectAttempts > 0)) {
                await this.autoReconnectSession();
            }
        });

        window.addEventListener('offline', () => {
            this.showToast('Network disconnected. Waiting for WiFi/LAN connection...', 'warning');
            this.setBadge('reconnecting', 'Network Offline (Waiting for WiFi/LAN)...');
        });
    }

    stopSession() {
        this.streamClient.disconnect();
        this.activeSession = null;
        this.dom.btnConnect.disabled = false;
        this.dom.btnDisconnect.disabled = true;
        if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = false;
        if (this.dom.btnSidebarDisconnect) this.dom.btnSidebarDisconnect.disabled = true;

        this.dom.placeholder.style.display = 'flex';
        this.dom.floatDeviceName.textContent = 'No Session';
        this.setBadge('idle', 'IDLE — No Session Active');
        this.resetTelemetry();

        if (this.dom.sidebarHudTarget) this.dom.sidebarHudTarget.textContent = 'None';
        if (this.dom.sidebarHudStatus) {
            this.dom.sidebarHudStatus.textContent = 'Idle / Ready';
            this.dom.sidebarHudStatus.className = 'hud-val';
        }

        this.showToast('Remote session terminated.', 'info');
    }

    onStreamStatusChange(status, message) {
        if (status === 'connected') {
            this.setBadge('connected', `Streaming: ${this.selectedDevice ? this.selectedDevice.hostname : 'Host'}`);
            this.dom.placeholder.style.display = 'none';
            this.dom.btnConnect.disabled = true;
            this.dom.btnDisconnect.disabled = false;
            if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = true;
            if (this.dom.btnSidebarDisconnect) this.dom.btnSidebarDisconnect.disabled = false;

            if (this.dom.sidebarHudTarget) this.dom.sidebarHudTarget.textContent = this.selectedDevice ? this.selectedDevice.hostname : 'Host';
            if (this.dom.sidebarHudStatus) {
                this.dom.sidebarHudStatus.textContent = 'Streaming Live';
                this.dom.sidebarHudStatus.className = 'hud-good';
            }
        } else if (status === 'connecting' || status === 'reconnecting') {
            this.setBadge('connecting', message);
            this.dom.btnConnect.disabled = true;
            this.dom.btnDisconnect.disabled = false;
            if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = true;
            if (this.dom.btnSidebarDisconnect) this.dom.btnSidebarDisconnect.disabled = false;
            if (this.dom.sidebarHudStatus) {
                this.dom.sidebarHudStatus.textContent = message;
                this.dom.sidebarHudStatus.className = 'hud-val';
            }
        } else {
            this.setBadge('idle', message);
            this.dom.placeholder.style.display = 'flex';
            this.dom.btnConnect.disabled = false;
            this.dom.btnDisconnect.disabled = true;
            if (this.dom.btnSidebarConnect) this.dom.btnSidebarConnect.disabled = false;
            if (this.dom.btnSidebarDisconnect) this.dom.btnSidebarDisconnect.disabled = true;
            if (this.dom.sidebarHudStatus) {
                this.dom.sidebarHudStatus.textContent = 'Idle / Ready';
                this.dom.sidebarHudStatus.className = 'hud-val';
            }
        }
    }

    onTelemetryUpdate({ fps, rttMs, bitrateKbps, remoteWidth, remoteHeight }) {
        this.dom.telemetryFps.textContent = `${fps} FPS`;
        this.dom.telemetryFps.className = `telemetry-val ${fps >= 30 ? 'good' : 'warn'}`;

        this.dom.telemetryRtt.textContent = `${rttMs} ms`;
        this.dom.telemetryRtt.className = `telemetry-val ${rttMs < 50 ? 'good' : 'warn'}`;

        this.dom.telemetryBitrate.textContent = `${bitrateKbps} kbps`;
        this.dom.telemetryRes.textContent = `${remoteWidth}×${remoteHeight}`;

        // Sync Sidebar Telemetry Card
        if (this.dom.sidebarHudRtt) this.dom.sidebarHudRtt.textContent = `${rttMs} ms`;
        if (this.dom.sidebarHudFps) this.dom.sidebarHudFps.textContent = `${fps} FPS`;
        if (this.dom.sidebarHudBitrate) this.dom.sidebarHudBitrate.textContent = `${bitrateKbps} kbps`;
        if (this.dom.sidebarHudRes) this.dom.sidebarHudRes.textContent = `${remoteWidth}×${remoteHeight}`;
    }

    resetTelemetry() {
        this.dom.telemetryFps.textContent = '-- FPS';
        this.dom.telemetryRtt.textContent = '-- ms';
        this.dom.telemetryBitrate.textContent = '-- kbps';
        this.dom.telemetryRes.textContent = '--×--';

        if (this.dom.sidebarHudRtt) this.dom.sidebarHudRtt.textContent = '-- ms';
        if (this.dom.sidebarHudFps) this.dom.sidebarHudFps.textContent = '-- FPS';
        if (this.dom.sidebarHudBitrate) this.dom.sidebarHudBitrate.textContent = '-- kbps';
        if (this.dom.sidebarHudRes) this.dom.sidebarHudRes.textContent = '--×--';
    }

    onCursorChange(cssCursor) {
        if (this.inputHandler.inputEnabled) {
            this.canvas.style.cursor = cssCursor;
        }
    }

    setBadge(type, text) {
        this.dom.statusBadge.className = `cyber-badge badge-${type}`;
        this.dom.statusText.textContent = text;
    }

    showToast(message, type = 'info') {
        const container = document.getElementById('toast-container');
        if (!container) return;

        const toast = document.createElement('div');
        toast.className = `cyber-toast ${type}`;
        toast.textContent = message;
        container.appendChild(toast);

        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(12px)';
            toast.style.transition = 'all 0.3s ease';
            setTimeout(() => toast.remove(), 300);
        }, 3200);
    }
}

// Instantiate Web Controller when DOM is ready
window.addEventListener('DOMContentLoaded', () => {
    window.app = new WebControllerApp();
});
