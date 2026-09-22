/*
 * ATTENTION: The "eval" devtool has been used (maybe by default in mode: "development").
 * This devtool is neither made for production nor for readable output files.
 * It uses "eval()" calls to create a separate source file in the browser devtools.
 * If you are trying to read the output file, select a different devtool (https://webpack.js.org/configuration/devtool/)
 * or disable the default devtool with "devtool: false".
 * If you are looking for production-ready output files, see mode: "production" (https://webpack.js.org/configuration/mode/).
 */
var pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad;
/******/ (() => { // webpackBootstrap
/******/ 	"use strict";
/******/ 	var __webpack_modules__ = ({

/***/ "./ScrollDownButton/index.ts"
/*!***********************************!*\
  !*** ./ScrollDownButton/index.ts ***!
  \***********************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   ScrollDownButton: () => (/* binding */ ScrollDownButton)\n/* harmony export */ });\n/** Một nút neo tối giản: tìm vùng có scrollbar gần control nhất và cuộn xuống cuối. */\nclass ScrollDownButton {\n  constructor() {\n    this.scrollDown = () => {\n      var _a, _b;\n      var target = this.findScrollableContainer();\n      var reduceMotion = (_b = (_a = window.matchMedia) === null || _a === void 0 ? void 0 : _a.call(window, \"(prefers-reduced-motion: reduce)\").matches) !== null && _b !== void 0 ? _b : false;\n      target.scrollTo({\n        top: target.scrollHeight,\n        behavior: reduceMotion ? \"auto\" : \"smooth\"\n      });\n    };\n  }\n  init(context, _notifyOutputChanged, _state, container) {\n    this.container = container;\n    this.button = document.createElement(\"button\");\n    this.button.type = \"button\";\n    this.button.className = \"fmc-scroll-down-button\";\n    this.button.textContent = \"↓\";\n    this.button.addEventListener(\"click\", this.scrollDown);\n    container.appendChild(this.button);\n    this.updateLabel(context.userSettings.languageId);\n  }\n  updateView(context) {\n    this.updateLabel(context.userSettings.languageId);\n  }\n  getOutputs() {\n    return {};\n  }\n  destroy() {\n    var _a;\n    (_a = this.button) === null || _a === void 0 ? void 0 : _a.removeEventListener(\"click\", this.scrollDown);\n    this.button = undefined;\n    this.container = undefined;\n  }\n  findScrollableContainer() {\n    var _a, _b, _c;\n    var current = (_b = (_a = this.container) === null || _a === void 0 ? void 0 : _a.parentElement) !== null && _b !== void 0 ? _b : null;\n    while (current) {\n      var overflowY = window.getComputedStyle(current).overflowY;\n      if (/(auto|scroll|overlay)/.test(overflowY) && current.scrollHeight > current.clientHeight + 1) return current;\n      current = current.parentElement;\n    }\n    return (_c = document.scrollingElement) !== null && _c !== void 0 ? _c : document.documentElement;\n  }\n  updateLabel(languageId) {\n    if (!this.button) return;\n    var label = languageId === 1066 ? \"Cuộn xuống cuối\" : \"Scroll to bottom\";\n    this.button.setAttribute(\"aria-label\", label);\n    this.button.title = label;\n  }\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./ScrollDownButton/index.ts?\n}");

/***/ }

/******/ 	});
/************************************************************************/
/******/ 	// The require scope
/******/ 	const __webpack_require__ = {};
/******/ 	
/************************************************************************/
/******/ 	/* webpack/runtime/define property getters */
/******/ 	// define getter/value functions for harmony exports
/******/ 	__webpack_require__.d = (exports, definition) => {
/******/ 		for(var key in definition) {
/******/ 			if(__webpack_require__.o(definition, key) && !__webpack_require__.o(exports, key)) {
/******/ 				Object.defineProperty(exports, key, { enumerable: true, get: definition[key] });
/******/ 			}
/******/ 		}
/******/ 	};
/******/ 	
/******/ 	/* webpack/runtime/hasOwnProperty shorthand */
/******/ 	__webpack_require__.o = (obj, prop) => (Object.prototype.hasOwnProperty.call(obj, prop));
/******/ 	
/******/ 	/* webpack/runtime/make namespace object */
/******/ 	// define __esModule on exports
/******/ 	__webpack_require__.r = (exports) => {
/******/ 		Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' });
/******/ 		Object.defineProperty(exports, '__esModule', { value: true });
/******/ 	};
/******/ 	
/************************************************************************/
/******/ 	
/******/ 	// startup
/******/ 	// Load entry module and return exports
/******/ 	// This entry module can't be inlined because the eval devtool is used.
/******/ 	let __webpack_exports__ = {};
/******/ 	__webpack_modules__["./ScrollDownButton/index.ts"](0,__webpack_exports__,__webpack_require__);
/******/ 	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = __webpack_exports__;
/******/ 	
/******/ })()
;
if (window.ComponentFramework && window.ComponentFramework.registerControl) {
	ComponentFramework.registerControl('FMC.Bms.ScrollDownButton', pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.ScrollDownButton);
} else {
	var FMC = FMC || {};
	FMC.Bms = FMC.Bms || {};
	FMC.Bms.ScrollDownButton = pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.ScrollDownButton;
	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = undefined;
}