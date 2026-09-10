// Developer-only form script. The server plugin remains the enforcement layer.
var FMC = window.FMC || {};
FMC.EquipmentPluginTest = (function () {
    "use strict";
    var testFormId = "4e2cf907-5c8b-4fa5-9bfb-55faf81fd889";
    function onLoad(executionContext) {
        var form = executionContext.getFormContext();
        var current = form.ui.formSelector.getCurrentItem();
        // Fail closed if accidentally attached to a different form.
        if (form.data.entity.getEntityName() !== "fmc_bmsequipment" ||
            (current && current.getId().replace(/[{}]/g, "").toLowerCase() !== testFormId)) return;
        var building = form.getAttribute("fmc_buildingid");
        if (!building) return;
        building.setRequiredLevel("none");
        form.ui.setFormNotification(
            "PLUGIN TEST (Developer): De trong Building roi bam Save de xem loi BMS-EQUIPMENT-001. " +
            "Nhap du Name, Equipment Code va Equipment Type. Form nay luu du lieu that vao Dataverse.",
            "WARNING", "fmc-plugin-test");
        var details = form.ui.tabs.get("fmc_bmsrelations");
        if (details) details.setFocus();
    }
    return { onLoad: onLoad };
}());
