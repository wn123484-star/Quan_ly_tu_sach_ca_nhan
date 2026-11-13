import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const formCustom = document.getElementById('formCustomMembership');

    const submitSwitch = async (targetTier, durationMonths, paymentGateway) => {
        const res = await apiFetch('/S16MembershipTier/SwitchMembership', {
            method: 'POST',
            body: {
                targetTier,
                durationMonths: parseInt(durationMonths, 10) || 12,
                paymentGateway: paymentGateway || 'VNPaySandbox'
            }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 700);
        }
    };

    document.querySelectorAll('.js-switch-tier').forEach(btn => {
        btn.addEventListener('click', async () => {
            const tier = btn.getAttribute('data-tier') || 'Premium';
            const months = btn.getAttribute('data-months') || '12';
            btn.disabled = true;
            try {
                await submitSwitch(tier, months, 'VNPaySandbox');
            } finally {
                btn.disabled = false;
            }
        });
    });

    formCustom?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const tier = document.getElementById('selectTargetTier')?.value || 'Premium';
        const months = document.getElementById('selectDurationMonths')?.value || '12';
        const gateway = document.getElementById('selectGateway')?.value || 'VNPaySandbox';
        await submitSwitch(tier, months, gateway);
    });
});
