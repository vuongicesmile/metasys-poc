// Script chỉ phục vụ form test dành cho developer; plugin server vẫn là lớp bắt buộc cuối cùng.
var FMC = window.FMC || {};
FMC.EquipmentPluginTest = (function () {
    "use strict";
    // Chỉ cho phép script chạy trên đúng test form đã được provision.
    var testFormId = "4e2cf907-5c8b-4fa5-9bfb-55faf81fd889";
    function onLoad(executionContext) {
        // Lấy form context thay vì dùng global form khi có nhiều form mở.
        var form = executionContext.getFormContext();
        var current = form.ui.formSelector.getCurrentItem();
        // Nếu gắn nhầm form/table thì dừng ngay, không thay đổi dữ liệu.
        if (form.data.entity.getEntityName() !== "fmc_bmsequipment" ||
            (current && current.getId().replace(/[{}]/g, "").toLowerCase() !== testFormId)) return;
        var building = form.getAttribute("fmc_buildingid");
        // Một số form có thể không chứa lookup này; khi đó không có gì cần test.
        if (!building) return;
        // Hạ required level chỉ trên form test để thử lỗi plugin khi Save.
        building.setRequiredLevel("none");
        form.ui.setFormNotification(
            "PLUGIN TEST (Developer): De trong Building roi bam Save de xem loi BMS-EQUIPMENT-001. " +
            "Nhap du Name, Equipment Code va Equipment Type. Form nay luu du lieu that vao Dataverse.",
            "WARNING", "fmc-plugin-test");
        // Đưa người dùng tới tab quan hệ để dễ quan sát lookup đang được test.
        var details = form.ui.tabs.get("fmc_bmsrelations");
        if (details) details.setFocus();
    }
    return { onLoad: onLoad };
}());
