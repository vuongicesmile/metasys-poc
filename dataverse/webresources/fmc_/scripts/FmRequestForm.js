var FMC = typeof window !== "undefined" ? (window.FMC = window.FMC || {}) : {};
FMC.FmRequestForm = (function () {
    "use strict";

    var TYPE_CIWG = 789140000;
    var TYPE_RISK = 789140001;
    var SEVERITY_MEDIUM = 789142001;
    var commonRequired = ["fmc_name", "fmc_description", "fmc_requesttype", "fmc_department"];
    var riskRequired = ["fmc_riskseverity", "fmc_likelihood", "fmc_impact", "fmc_mitigation", "fmc_reviewdate"];

    function attribute(form, name) { return form.getAttribute(name); }
    function required(form, names, level) {
        names.forEach(function (name) {
            var value = attribute(form, name);
            if (value) value.setRequiredLevel(level);
        });
    }
    function setDefault(form, name, value) {
        var field = attribute(form, name);
        if (field && field.getValue() === null) field.setValue(value);
    }
    function applyRequirements(form) {
        required(form, commonRequired, "required");
        var type = attribute(form, "fmc_requesttype");
        var isRisk = !!type && type.getValue() === TYPE_RISK;
        required(form, riskRequired, isRisk ? "required" : "none");
        if (isRisk) setDefault(form, "fmc_riskseverity", SEVERITY_MEDIUM);
    }
    async function copyBuildingCode(form) {
        var lookup = attribute(form, "fmc_buildingid");
        var code = attribute(form, "fmc_buildingcode");
        if (!lookup || !code) return;
        var selected = lookup.getValue();
        if (!selected || !selected.length) { code.setValue(null); return; }
        var id = selected[0].id.replace(/[{}]/g, "");
        var row = await Xrm.WebApi.retrieveRecord("fmc_bmsbuilding", id, "?$select=fmc_buildingcode");
        code.setValue(row.fmc_buildingcode || null);
        code.setSubmitMode("always");
    }
    function onLoad(executionContext) {
        var form = executionContext.getFormContext();
        if (form.data.entity.getEntityName() !== "fmc_fmrequest") return;
        setDefault(form, "fmc_requesttype", TYPE_CIWG);
        setDefault(form, "fmc_estimatedvalue", 0);
        applyRequirements(form);
        var type = attribute(form, "fmc_requesttype");
        if (type) type.addOnChange(function () { applyRequirements(form); });
        var building = attribute(form, "fmc_buildingid");
        if (building) building.addOnChange(function () {
            copyBuildingCode(form).catch(function (error) {
                form.ui.setFormNotification("Unable to copy Building Code: " + error.message, "ERROR", "fmc-building-code");
            });
        });
    }
    return { onLoad: onLoad, applyRequirements: applyRequirements, copyBuildingCode: copyBuildingCode };
}());

if (typeof module !== "undefined" && module.exports) module.exports = FMC.FmRequestForm;
