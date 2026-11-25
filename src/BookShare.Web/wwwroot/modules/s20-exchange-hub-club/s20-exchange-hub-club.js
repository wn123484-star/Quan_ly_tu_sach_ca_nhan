import { apiFetch, showHandoverQrModal, confirmDialog } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const escAttr = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

    // 0. Admin thành lập CLB / Điểm trạm mới và chỉ định Trưởng nhóm
    document.getElementById('btnOpenCreateClubModal')?.addEventListener('click', async () => {
        if (typeof window.Swal === 'undefined') return;
        const leaderOptsHtml = document.getElementById('s20CandidateLeaderOptions')?.innerHTML || '';

        const { value: formValues } = await window.Swal.fire({
            title: 'Thành Lập Câu Lạc Bộ Sách Mới',
            width: 580,
            html: `
                <div class="text-start small">
                    <label class="fw-bold mb-1">Tên Câu lạc bộ / Điểm trạm *</label>
                    <input id="swalNewClubName" class="form-control form-control-sm mb-2" placeholder="VD: CLB Đọc Sách Công Nghệ CMC" />
                    <label class="fw-bold mb-1">Địa chỉ sinh hoạt &amp; giao nhận *</label>
                    <input id="swalNewClubAddr" class="form-control form-control-sm mb-2" placeholder="VD: Tầng 2, Tòa nhà CMC, Cầu Giấy" />
                    <div class="row g-2 mb-2">
                        <div class="col-6">
                            <label class="fw-bold mb-1">Loại hình</label>
                            <select id="swalNewClubType" class="form-select form-select-sm">
                                <option value="ClubStation" selected>⛺ Câu lạc bộ Sách</option>
                                <option value="DropoffHub">📍 Điểm ký gửi cộng đồng</option>
                                <option value="Library">🏛️ Thư viện Đối tác</option>
                            </select>
                        </div>
                        <div class="col-6">
                            <label class="fw-bold mb-1">Chỉ định Trưởng nhóm *</label>
                            <select id="swalNewClubLeader" class="form-select form-select-sm">${leaderOptsHtml}</select>
                        </div>
                    </div>
                    <div class="row g-2 mb-2">
                        <div class="col-6">
                            <label class="fw-bold mb-1">Giờ hoạt động</label>
                            <input id="swalNewClubHours" class="form-control form-control-sm" value="09:00 - 20:00 (Cả tuần)" />
                        </div>
                        <div class="col-6">
                            <label class="fw-bold mb-1">Chế độ gia nhập</label>
                            <select id="swalNewClubPublic" class="form-select form-select-sm">
                                <option value="1" selected>Nhóm mở (Tham gia ngay)</option>
                                <option value="0">Nhóm kiểm duyệt (Trưởng nhóm duyệt)</option>
                            </select>
                        </div>
                    </div>
                    <label class="fw-bold mb-1">Giới thiệu CLB</label>
                    <textarea id="swalNewClubDesc" class="form-control form-control-sm mb-2" rows="2" placeholder="Sứ mệnh, chủ đề sách và lịch offline định kỳ..."></textarea>
                    <label class="fw-bold mb-1">Điều lệ &amp; Nội quy</label>
                    <textarea id="swalNewClubRules" class="form-control form-control-sm" rows="2">1. Chia sẻ sách giấy 100% phi thương mại (0đ). 2. Giữ gìn sách sạch đẹp và trả đúng hẹn.</textarea>
                </div>`,
            showCancelButton: true,
            confirmButtonText: 'Thành lập CLB ngay',
            cancelButtonText: 'Hủy',
            confirmButtonColor: '#2563eb',
            preConfirm: () => {
                const name = document.getElementById('swalNewClubName')?.value.trim() || '';
                const address = document.getElementById('swalNewClubAddr')?.value.trim() || '';
                if (!name || !address) {
                    window.Swal.showValidationMessage('Vui lòng nhập đầy đủ Tên CLB và Địa chỉ');
                    return null;
                }
                return {
                    name,
                    address,
                    hubType: document.getElementById('swalNewClubType')?.value || 'ClubStation',
                    leaderUserId: document.getElementById('swalNewClubLeader')?.value || '',
                    openHours: document.getElementById('swalNewClubHours')?.value.trim() || '09:00 - 20:00',
                    isPublicClub: document.getElementById('swalNewClubPublic')?.value === '1',
                    description: document.getElementById('swalNewClubDesc')?.value.trim() || '',
                    clubRules: document.getElementById('swalNewClubRules')?.value.trim() || ''
                };
            }
        });

        if (!formValues) return;
        const res = await apiFetch('/S20ExchangeHubClub/CreateClub', { method: 'POST', body: formValues });
        if (res.ok) {
            const newId = res.data?.hubId;
            window.location.href = newId ? `/S20ExchangeHubClub?hubId=${newId}` : '/S20ExchangeHubClub';
        }
    });

    // 0b. Tham gia / Rời CLB
    document.getElementById('btnJoinSelectedClub')?.addEventListener('click', async (e) => {
        const btn = e.currentTarget;
        const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
        if (!hubId) return;
        const res = await apiFetch('/S20ExchangeHubClub/JoinClub', { method: 'POST', body: { hubId }, button: btn });
        if (res.ok) setTimeout(() => window.location.reload(), 600);
    });

    document.getElementById('btnLeaveSelectedClub')?.addEventListener('click', async (e) => {
        const btn = e.currentTarget;
        const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
        if (!hubId) return;
        const ok = await confirmDialog({
            title: 'Rời khỏi nhóm CLB này?',
            text: 'Bạn có thể đăng ký tham gia lại bất cứ lúc nào.',
            confirmText: 'Rời nhóm',
            danger: true
        });
        if (!ok) return;
        const res = await apiFetch('/S20ExchangeHubClub/LeaveClub', { method: 'POST', body: { hubId }, button: btn });
        if (res.ok) setTimeout(() => window.location.reload(), 600);
    });

    // 0c. Thêm thành viên vào nhóm CLB như Group
    document.getElementById('formAddClubMember')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const hubId = parseInt(document.getElementById('addMemberHubId')?.value || '0', 10);
        const targetUserId = document.getElementById('addMemberUserId')?.value || '';
        const targetEmail = document.getElementById('addMemberEmail')?.value.trim() || '';
        const memberRole = document.getElementById('addMemberRole')?.value || 'Member';
        const res = await apiFetch('/S20ExchangeHubClub/AddClubMember', {
            method: 'POST',
            body: { hubId, targetUserId, targetEmail, memberRole }
        });
        if (res.ok) setTimeout(() => window.location.reload(), 600);
    });

    // 0d. Bổ nhiệm / Chuyển giao Trưởng nhóm CLB
    document.getElementById('btnAssignClubLeader')?.addEventListener('click', async (e) => {
        const btn = e.currentTarget;
        const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
        const leaderUserId = document.getElementById('assignLeaderUserId')?.value || '';
        if (!hubId || !leaderUserId) return;
        const ok = await confirmDialog({
            title: 'Chuyển giao quyền Trưởng nhóm CLB?',
            text: 'Thành viên được chọn sẽ trở thành Trưởng nhóm chính thức quản lý thành viên, bảng tin và điều phối sách của CLB này.',
            confirmText: 'Xác nhận bổ nhiệm',
            danger: false
        });
        if (!ok) return;
        const res = await apiFetch('/S20ExchangeHubClub/AssignClubLeader', {
            method: 'POST',
            body: { hubId, leaderUserId },
            button: btn
        });
        if (res.ok) setTimeout(() => window.location.reload(), 650);
    });

    // 0e. Duyệt / Từ chối / Phân vai trò / Mời rời thành viên CLB
    document.querySelectorAll('.js-club-review-member').forEach(btn => {
        btn.addEventListener('click', async () => {
            const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
            const memberId = parseInt(btn.getAttribute('data-member-id') || '0', 10);
            const approve = btn.getAttribute('data-approve') === 'true';
            const res = await apiFetch('/S20ExchangeHubClub/ReviewClubMember', {
                method: 'POST',
                body: { hubId, memberId, approve },
                button: btn
            });
            if (res.ok) setTimeout(() => window.location.reload(), 550);
        });
    });

    document.querySelectorAll('.js-club-role-member').forEach(btn => {
        btn.addEventListener('click', async () => {
            const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
            const memberId = parseInt(btn.getAttribute('data-member-id') || '0', 10);
            const newRole = btn.getAttribute('data-new-role') || 'Member';
            const res = await apiFetch('/S20ExchangeHubClub/UpdateClubMemberRole', {
                method: 'POST',
                body: { hubId, memberId, newRole },
                button: btn
            });
            if (res.ok) setTimeout(() => window.location.reload(), 550);
        });
    });

    document.querySelectorAll('.js-club-remove-member').forEach(btn => {
        btn.addEventListener('click', async () => {
            const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
            const memberId = parseInt(btn.getAttribute('data-member-id') || '0', 10);
            const memberName = btn.getAttribute('data-member-name') || 'thành viên này';
            const ok = await confirmDialog({
                title: `Mời "${memberName}" rời khỏi nhóm CLB?`,
                text: 'Thành viên này sẽ không còn trong danh sách nhóm CLB hiện tại.',
                confirmText: 'Xác nhận mời rời',
                danger: true
            });
            if (!ok) return;
            const res = await apiFetch('/S20ExchangeHubClub/RemoveClubMember', {
                method: 'POST',
                body: { hubId, memberId },
                button: btn
            });
            if (res.ok) setTimeout(() => window.location.reload(), 550);
        });
    });

    // 0f. Đăng / Xóa bài trên Bảng tin nội bộ CLB
    document.getElementById('formCreateClubPost')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const hubId = parseInt(document.getElementById('clubPostHubId')?.value || '0', 10);
        const title = document.getElementById('clubPostTitle')?.value.trim() || '';
        const content = document.getElementById('clubPostContent')?.value.trim() || '';
        const isPinned = Boolean(document.getElementById('clubPostPinned')?.checked);
        const res = await apiFetch('/S20ExchangeHubClub/CreateClubPost', {
            method: 'POST',
            body: { hubId, title, content, isPinned }
        });
        if (res.ok) setTimeout(() => window.location.reload(), 550);
    });

    document.querySelectorAll('.js-club-delete-post').forEach(btn => {
        btn.addEventListener('click', async () => {
            const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
            const postId = parseInt(btn.getAttribute('data-post-id') || '0', 10);
            const res = await apiFetch('/S20ExchangeHubClub/DeleteClubPost', {
                method: 'POST',
                body: { hubId, postId },
                button: btn
            });
            if (res.ok) setTimeout(() => window.location.reload(), 500);
        });
    });

    // 1. User đăng ký ký gửi sách P2P tại Trạm Hub
    document.getElementById('formRegisterDropoff')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookCopyId = parseInt(document.getElementById('dropoffCopyId')?.value || '0', 10);
        const hubId = parseInt(document.getElementById('dropoffHubId')?.value || '3', 10);

        const res = await apiFetch('/S20ExchangeHubClub/RegisterDropoff', {
            method: 'POST',
            body: { bookCopyId, hubId }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 2. Curator / Trưởng nhóm CLB xác nhận kiểm định sách thật
    document.getElementById('formCuratorVerify')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookCopyId = parseInt(document.getElementById('verifyCopyId')?.value || '0', 10);
        const hubId = parseInt(document.getElementById('verifyHubId')?.value || '3', 10);
        const verifiedCondition = document.getElementById('verifyCondition')?.value || 'Good';
        const shelfLocation = document.getElementById('verifyShelfLocation')?.value || 'Trạm CLB-K1';

        const res = await apiFetch('/S20ExchangeHubClub/VerifyPhysicalBook', {
            method: 'POST',
            body: { bookCopyId, hubId, verifiedCondition, shelfLocation }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 3. Curator xác nhận ký gửi / bàn giao / thu hồi trung gian tại Hub
    document.querySelectorAll('.js-hub-action').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const actionType = btn.getAttribute('data-action') || 'CheckInDropoff';

            const res = await apiFetch('/S20ExchangeHubClub/ProcessHubHandover', {
                method: 'POST',
                body: { loanRequestId, actionType },
                button: btn
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });

    // 4. Chỉnh sửa thông tin, mô tả & điều lệ CLB / Điểm trạm
    document.querySelectorAll('.js-s20-edit-hub').forEach(btn => {
        btn.addEventListener('click', async () => {
            const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
            const curName = btn.getAttribute('data-hub-name') || '';
            const curAddress = btn.getAttribute('data-hub-address') || '';
            const curHours = btn.getAttribute('data-hub-hours') || '08:00 - 18:00';
            const curPhone = btn.getAttribute('data-hub-phone') || '';
            const curDesc = btn.getAttribute('data-hub-desc') || '';
            const curRules = btn.getAttribute('data-hub-rules') || '';
            const curPub = btn.getAttribute('data-hub-public') !== '0';
            if (!hubId || typeof window.Swal === 'undefined') return;

            const { value: formValues } = await window.Swal.fire({
                title: `Cập nhật Điểm trạm / CLB #${hubId}`,
                width: 560,
                html:
                    `<div class="text-start small">` +
                    `<label class="fw-bold mb-1">Tên Câu lạc bộ / Điểm trạm *</label>` +
                    `<input id="swalHubName" class="form-control form-control-sm mb-2" value="${escAttr(curName)}">` +
                    `<label class="fw-bold mb-1">Địa chỉ hoạt động *</label>` +
                    `<input id="swalHubAddr" class="form-control form-control-sm mb-2" value="${escAttr(curAddress)}">` +
                    `<div class="row g-2 mb-2">` +
                    `<div class="col-6"><label class="fw-bold mb-1">Giờ mở cửa</label><input id="swalHubHours" class="form-control form-control-sm" value="${escAttr(curHours)}"></div>` +
                    `<div class="col-6"><label class="fw-bold mb-1">Hotline liên hệ</label><input id="swalHubPhone" class="form-control form-control-sm" value="${escAttr(curPhone)}"></div>` +
                    `</div>` +
                    `<label class="fw-bold mb-1">Chế độ gia nhập nhóm</label>` +
                    `<select id="swalHubPublic" class="form-select form-select-sm mb-2">` +
                    `<option value="1" ${curPub ? 'selected' : ''}>Nhóm mở (Tham gia trực tiếp)</option>` +
                    `<option value="0" ${!curPub ? 'selected' : ''}>Nhóm kiểm duyệt (Trưởng nhóm phê duyệt)</option>` +
                    `</select>` +
                    `<label class="fw-bold mb-1">Giới thiệu CLB</label>` +
                    `<textarea id="swalHubDesc" class="form-control form-control-sm mb-2" rows="2">${escAttr(curDesc)}</textarea>` +
                    `<label class="fw-bold mb-1">Điều lệ &amp; Nội quy</label>` +
                    `<textarea id="swalHubRules" class="form-control form-control-sm" rows="2">${escAttr(curRules)}</textarea>` +
                    `</div>`,
                focusConfirm: false,
                showCancelButton: true,
                confirmButtonText: 'Lưu cập nhật',
                cancelButtonText: 'Hủy',
                confirmButtonColor: '#2563eb',
                preConfirm: () => {
                    const name = document.getElementById('swalHubName')?.value.trim() || '';
                    const address = document.getElementById('swalHubAddr')?.value.trim() || '';
                    if (!name || !address) {
                        window.Swal.showValidationMessage('Vui lòng nhập đầy đủ Tên trạm và Địa chỉ');
                        return null;
                    }
                    return {
                        hubId,
                        name,
                        address,
                        openHours: document.getElementById('swalHubHours')?.value.trim() || '08:00 - 18:00',
                        phone: document.getElementById('swalHubPhone')?.value.trim() || '',
                        description: document.getElementById('swalHubDesc')?.value.trim() || '',
                        clubRules: document.getElementById('swalHubRules')?.value.trim() || '',
                        isPublicClub: document.getElementById('swalHubPublic')?.value === '1'
                    };
                }
            });

            if (!formValues) return;

            const res = await apiFetch('/S20ExchangeHubClub/UpdateHubStation', {
                method: 'POST',
                body: formValues,
                button: btn
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    document.querySelectorAll('.js-qr-create').forEach(btn => {
        btn.addEventListener('click', () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const handoverType = btn.getAttribute('data-type') || 'Handover';
            if (loanRequestId) showHandoverQrModal({ loanRequestId, handoverType });
        });
    });
});
