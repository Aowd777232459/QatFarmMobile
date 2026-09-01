window.toggleSidebar = function () {
    const sidebar = document.getElementById('sidebar');
    if (sidebar) sidebar.classList.toggle('open');
};
document.addEventListener('click', function (event) {
    const sidebar = document.getElementById('sidebar');
    if (!sidebar || window.innerWidth > 900) return;
    if (!sidebar.contains(event.target) && !event.target.closest('.mobile-only')) sidebar.classList.remove('open');
});


// ===== QatFarm Mobile PWA =====
let qatFarmInstallPrompt = null;
window.addEventListener('beforeinstallprompt', (event) => {
    event.preventDefault();
    qatFarmInstallPrompt = event;
    document.documentElement.classList.add('pwa-install-available');
    window.dispatchEvent(new CustomEvent('qatfarm-install-available'));
});
window.addEventListener('appinstalled', () => {
    qatFarmInstallPrompt = null;
    document.documentElement.classList.remove('pwa-install-available');
});
window.installQatFarmApp = async function () {
    if (!qatFarmInstallPrompt) return false;
    qatFarmInstallPrompt.prompt();
    const result = await qatFarmInstallPrompt.userChoice;
    if (result.outcome === 'accepted') qatFarmInstallPrompt = null;
    return result.outcome === 'accepted';
};
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => navigator.serviceWorker.register('/service-worker.js').catch(() => {}));
}

// Close the mobile drawer after choosing a navigation item.
document.addEventListener('click', function (event) {
    const item = event.target.closest('.nav-item');
    const sidebar = document.getElementById('sidebar');
    if (item && sidebar && window.innerWidth <= 900) sidebar.classList.remove('open');
});
