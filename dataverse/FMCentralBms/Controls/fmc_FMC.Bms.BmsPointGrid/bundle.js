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

/***/ "./BmsPointGrid/Business/Services/PointGridService.ts"
/*!************************************************************!*\
  !*** ./BmsPointGrid/Business/Services/PointGridService.ts ***!
  \************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   PointGridService: () => (/* binding */ PointGridService)\n/* harmony export */ });\n/* harmony import */ var _Domain_Policies_ReadingFreshness__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! ../../Domain/Policies/ReadingFreshness */ \"./BmsPointGrid/Domain/Policies/ReadingFreshness.ts\");\n\n/** Chuyển dữ liệu thô thành DTO an toàn để màn hình dùng. */\nclass PointGridService {\n  createViewModel(snapshot, options) {\n    return {\n      rows: snapshot.rows.map(point => {\n        var _a, _b, _c;\n        return {\n          id: point.id,\n          // Fallback chỉ phục vụ nhãn màn hình; không thay đổi Object ID lưu trong Dataverse.\n          objectId: (_a = nonEmptyText(point.objectId)) !== null && _a !== void 0 ? _a : \"—\",\n          name: (_c = (_b = nonEmptyText(point.name)) !== null && _b !== void 0 ? _b : nonEmptyText(point.objectId)) !== null && _c !== void 0 ? _c : \"(Unnamed Point)\",\n          currentValue: point.currentValue,\n          unit: nonEmptyText(point.unit),\n          lastReadingTimeUtc: point.lastReadingTimeUtc,\n          sourceSystem: nonEmptyText(point.sourceSystem),\n          freshness: (0,_Domain_Policies_ReadingFreshness__WEBPACK_IMPORTED_MODULE_0__.classifyReadingFreshness)(point.lastReadingTimeUtc, options.now, options.staleAfterMinutes)\n        };\n      }),\n      isLoading: snapshot.isLoading,\n      errorMessage: snapshot.errorMessage,\n      hasNextPage: snapshot.hasNextPage,\n      hasPreviousPage: snapshot.hasPreviousPage,\n      totalResultCount: snapshot.totalResultCount\n    };\n  }\n}\n/** Chuỗi toàn khoảng trắng không hữu ích trên UI nên được xem là dữ liệu thiếu. */\nfunction nonEmptyText(value) {\n  if (value === undefined) {\n    return undefined;\n  }\n  var trimmed = value.trim();\n  return trimmed.length > 0 ? trimmed : undefined;\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/Business/Services/PointGridService.ts?\n}");

/***/ },

/***/ "./BmsPointGrid/Common/Localization/messages.ts"
/*!******************************************************!*\
  !*** ./BmsPointGrid/Common/Localization/messages.ts ***!
  \******************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   getText: () => (/* binding */ getText)\n/* harmony export */ });\nvar EN = {\n  title: \"Current BMS Points\",\n  refresh: \"Refresh\",\n  previous: \"Previous\",\n  next: \"Next\",\n  loading: \"Loading points…\",\n  empty: \"No points are available in this view.\",\n  errorPrefix: \"Unable to load points\",\n  point: \"Point\",\n  objectId: \"Object ID\",\n  currentValue: \"Current value\",\n  readingTime: \"Reading time\",\n  source: \"Source\",\n  freshness: {\n    fresh: \"Current\",\n    stale: \"Stale\",\n    unknown: \"Unknown\"\n  },\n  total: count => \"\".concat(count, \" point(s)\"),\n  rowAction: name => \"Open \".concat(name)\n};\nvar VI = {\n  title: \"Điểm BMS hiện tại\",\n  refresh: \"Làm mới\",\n  previous: \"Trang trước\",\n  next: \"Trang sau\",\n  loading: \"Đang tải các điểm…\",\n  empty: \"View này chưa có Point nào.\",\n  errorPrefix: \"Không thể tải Point\",\n  point: \"Point\",\n  objectId: \"Object ID\",\n  currentValue: \"Giá trị hiện tại\",\n  readingTime: \"Thời điểm đọc\",\n  source: \"Nguồn\",\n  freshness: {\n    fresh: \"Mới\",\n    stale: \"Cũ\",\n    unknown: \"Chưa rõ\"\n  },\n  total: count => \"\".concat(count, \" Point\"),\n  rowAction: name => \"M\\u1EDF \".concat(name)\n};\n/** Model-driven app trả LCID 1066 cho Vietnamese; các ngôn ngữ khác fallback English. */\nfunction getText(languageId) {\n  return languageId === 1066 ? VI : EN;\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/Common/Localization/messages.ts?\n}");

/***/ },

/***/ "./BmsPointGrid/DataAccess/Adapters/PcfPointDataSetAdapter.ts"
/*!********************************************************************!*\
  !*** ./BmsPointGrid/DataAccess/Adapters/PcfPointDataSetAdapter.ts ***!
  \********************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   PcfPointDataSetAdapter: () => (/* binding */ PcfPointDataSetAdapter)\n/* harmony export */ });\n/* harmony import */ var _Mappers_PointRecordMapper__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! ../Mappers/PointRecordMapper */ \"./BmsPointGrid/DataAccess/Mappers/PointRecordMapper.ts\");\n\n/**\n * Adapter duy nhất biết DataSet API của PCF.\n * View/subgrid của host quyết định filter, sort và quyền; control chỉ đọc kết quả đã cấp.\n */\nclass PcfPointDataSetAdapter {\n  constructor() {\n    this.isPagingRequestInFlight = false;\n  }\n  /** updateView gọi hàm này trước khi Presentation đọc snapshot mới. */\n  update(dataSet) {\n    this.dataSet = dataSet;\n    // Power Apps gửi updateView khi paging hoàn thành; lúc đó mới cho phép yêu cầu trang tiếp theo.\n    if (!dataSet.loading) {\n      this.isPagingRequestInFlight = false;\n    }\n  }\n  getSnapshot() {\n    var _a;\n    if (!this.dataSet) {\n      return emptySnapshot();\n    }\n    var rows = this.dataSet.sortedRecordIds.map(id => {\n      var _a;\n      return (_a = this.dataSet) === null || _a === void 0 ? void 0 : _a.records[id];\n    }).filter(record => record !== undefined).map(record => (0,_Mappers_PointRecordMapper__WEBPACK_IMPORTED_MODULE_0__.mapPointRecord)(record));\n    var totalResultCount = this.dataSet.paging.totalResultCount;\n    return {\n      rows,\n      isLoading: this.dataSet.loading,\n      errorMessage: this.dataSet.error ? (_a = this.dataSet.errorMessage) !== null && _a !== void 0 ? _a : \"Power Apps could not load points.\" : undefined,\n      hasNextPage: this.dataSet.paging.hasNextPage,\n      hasPreviousPage: this.dataSet.paging.hasPreviousPage,\n      // -1 nghĩa host không biết tổng, không hiển thị thành một số giả.\n      totalResultCount: totalResultCount >= 0 ? totalResultCount : undefined\n    };\n  }\n  refresh() {\n    if (!this.dataSet || this.dataSet.loading) {\n      return;\n    }\n    // refresh/reset do host thực hiện và sẽ quay lại updateView với snapshot mới.\n    this.dataSet.refresh();\n  }\n  loadNextPage() {\n    var _a, _b;\n    if (!this.canChangePage((_a = this.dataSet) === null || _a === void 0 ? void 0 : _a.paging.hasNextPage)) {\n      return;\n    }\n    this.isPagingRequestInFlight = true;\n    (_b = this.dataSet) === null || _b === void 0 ? void 0 : _b.paging.loadNextPage();\n  }\n  loadPreviousPage() {\n    var _a, _b;\n    if (!this.canChangePage((_a = this.dataSet) === null || _a === void 0 ? void 0 : _a.paging.hasPreviousPage)) {\n      return;\n    }\n    this.isPagingRequestInFlight = true;\n    (_b = this.dataSet) === null || _b === void 0 ? void 0 : _b.paging.loadPreviousPage();\n  }\n  canChangePage(hasRequestedPage) {\n    return Boolean(this.dataSet && hasRequestedPage && !this.dataSet.loading && !this.isPagingRequestInFlight);\n  }\n}\n/** Giá trị an toàn trong lần render hiếm hoi trước khi dataset được Power Apps cấp. */\nfunction emptySnapshot() {\n  return {\n    rows: [],\n    isLoading: true,\n    hasNextPage: false,\n    hasPreviousPage: false\n  };\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/DataAccess/Adapters/PcfPointDataSetAdapter.ts?\n}");

/***/ },

/***/ "./BmsPointGrid/DataAccess/Adapters/PcfPointNavigationAdapter.ts"
/*!***********************************************************************!*\
  !*** ./BmsPointGrid/DataAccess/Adapters/PcfPointNavigationAdapter.ts ***!
  \***********************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   PcfPointNavigationAdapter: () => (/* binding */ PcfPointNavigationAdapter)\n/* harmony export */ });\nvar __awaiter = undefined && undefined.__awaiter || function (thisArg, _arguments, P, generator) {\n  function adopt(value) {\n    return value instanceof P ? value : new P(function (resolve) {\n      resolve(value);\n    });\n  }\n  return new (P || (P = Promise))(function (resolve, reject) {\n    function fulfilled(value) {\n      try {\n        step(generator.next(value));\n      } catch (e) {\n        reject(e);\n      }\n    }\n    function rejected(value) {\n      try {\n        step(generator[\"throw\"](value));\n      } catch (e) {\n        reject(e);\n      }\n    }\n    function step(result) {\n      result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected);\n    }\n    step((generator = generator.apply(thisArg, _arguments || [])).next());\n  });\n};\n/** Adapter bao bọc navigation API để UI không phụ thuộc URL hay organization ID. */\nclass PcfPointNavigationAdapter {\n  constructor(navigation) {\n    this.navigation = navigation;\n  }\n  /** Power Apps có thể thay context giữa các lần updateView nên luôn nhận navigation mới nhất. */\n  update(navigation) {\n    this.navigation = navigation;\n  }\n  openPoint(id) {\n    return __awaiter(this, void 0, void 0, function* () {\n      if (!id) {\n        return;\n      }\n      yield this.navigation.openForm({\n        entityName: \"fmc_bmspoint\",\n        entityId: id\n      });\n    });\n  }\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/DataAccess/Adapters/PcfPointNavigationAdapter.ts?\n}");

/***/ },

/***/ "./BmsPointGrid/DataAccess/Mappers/PointRecordMapper.ts"
/*!**************************************************************!*\
  !*** ./BmsPointGrid/DataAccess/Mappers/PointRecordMapper.ts ***!
  \**************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   mapPointRecord: () => (/* binding */ mapPointRecord)\n/* harmony export */ });\n/** Chuyển EntityRecord của Power Apps thành model Domain thuần. */\nfunction mapPointRecord(record) {\n  return {\n    id: record.getRecordId(),\n    objectId: asText(record.getValue(\"fmc_objectid\")),\n    name: asText(record.getValue(\"fmc_name\")),\n    currentValue: asNumber(record.getValue(\"fmc_currentvalue\")),\n    unit: asText(record.getValue(\"fmc_unit\")),\n    lastReadingTimeUtc: asDate(record.getValue(\"fmc_lastreadingtime\")),\n    sourceSystem: asText(record.getValue(\"fmc_sourcesystem\"))\n  };\n}\n/** Chuỗi rỗng vẫn được giữ để Business quyết định fallback hiển thị. */\nfunction asText(value) {\n  return typeof value === \"string\" ? value : undefined;\n}\n/** Chỉ nhận số hữu hạn; không đổi chuỗi lỗi hoặc giá trị thiếu thành 0. */\nfunction asNumber(value) {\n  return typeof value === \"number\" && Number.isFinite(value) ? value : undefined;\n}\n/** Dataset trả Date cho cột Dataverse DateTime; clone để UI không sửa object gốc. */\nfunction asDate(value) {\n  if (!(value instanceof Date) || Number.isNaN(value.getTime())) {\n    return undefined;\n  }\n  return new Date(value.getTime());\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/DataAccess/Mappers/PointRecordMapper.ts?\n}");

/***/ },

/***/ "./BmsPointGrid/Domain/Policies/ReadingFreshness.ts"
/*!**********************************************************!*\
  !*** ./BmsPointGrid/Domain/Policies/ReadingFreshness.ts ***!
  \**********************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   classifyReadingFreshness: () => (/* binding */ classifyReadingFreshness)\n/* harmony export */ });\n/**\n * So sánh bằng UTC để kết quả không thay đổi theo timezone máy người dùng.\n * Reading ở tương lai được xem là fresh thay vì stale do clock lệch.\n */\nfunction classifyReadingFreshness(lastReadingTimeUtc, now, staleAfterMinutes) {\n  if (!lastReadingTimeUtc || Number.isNaN(lastReadingTimeUtc.getTime())) {\n    return \"unknown\";\n  }\n  var safeMinutes = Number.isFinite(staleAfterMinutes) && staleAfterMinutes > 0 ? staleAfterMinutes : 30;\n  var ageMilliseconds = now.getTime() - lastReadingTimeUtc.getTime();\n  return ageMilliseconds >= safeMinutes * 60000 ? \"stale\" : \"fresh\";\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/Domain/Policies/ReadingFreshness.ts?\n}");

/***/ },

/***/ "./BmsPointGrid/Presentation/Components/GridState.tsx"
/*!************************************************************!*\
  !*** ./BmsPointGrid/Presentation/Components/GridState.tsx ***!
  \************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   GridState: () => (/* binding */ GridState)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n\n\n/** Trạng thái load/empty/error được tách để table chính chỉ lo hiển thị rows. */\nfunction GridState(props) {\n  if (props.isLoading) {\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n      className: \"fmc-point-grid__state\",\n      role: \"status\"\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Spinner, {\n      size: \"small\"\n    }), \" \", props.loadingText);\n  }\n  if (props.errorMessage) {\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n      className: \"fmc-point-grid__state fmc-point-grid__state--error\",\n      role: \"alert\"\n    }, props.errorPrefix, \": \", props.errorMessage);\n  }\n  return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: \"fmc-point-grid__state\"\n  }, props.emptyText);\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/Presentation/Components/GridState.tsx?\n}");

/***/ },

/***/ "./BmsPointGrid/Presentation/Components/PointGrid.tsx"
/*!************************************************************!*\
  !*** ./BmsPointGrid/Presentation/Components/PointGrid.tsx ***!
  \************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   PointGrid: () => (/* binding */ PointGrid)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n/* harmony import */ var _GridState__WEBPACK_IMPORTED_MODULE_2__ = __webpack_require__(/*! ./GridState */ \"./BmsPointGrid/Presentation/Components/GridState.tsx\");\n/* harmony import */ var _ReadingValue__WEBPACK_IMPORTED_MODULE_3__ = __webpack_require__(/*! ./ReadingValue */ \"./BmsPointGrid/Presentation/Components/ReadingValue.tsx\");\nfunction _slicedToArray(r, e) { return _arrayWithHoles(r) || _iterableToArrayLimit(r, e) || _unsupportedIterableToArray(r, e) || _nonIterableRest(); }\nfunction _nonIterableRest() { throw new TypeError(\"Invalid attempt to destructure non-iterable instance.\\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.\"); }\nfunction _unsupportedIterableToArray(r, a) { if (r) { if (\"string\" == typeof r) return _arrayLikeToArray(r, a); var t = {}.toString.call(r).slice(8, -1); return \"Object\" === t && r.constructor && (t = r.constructor.name), \"Map\" === t || \"Set\" === t ? Array.from(r) : \"Arguments\" === t || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(t) ? _arrayLikeToArray(r, a) : void 0; } }\nfunction _arrayLikeToArray(r, a) { (null == a || a > r.length) && (a = r.length); for (var e = 0, n = Array(a); e < a; e++) n[e] = r[e]; return n; }\nfunction _iterableToArrayLimit(r, l) { var t = null == r ? null : \"undefined\" != typeof Symbol && r[Symbol.iterator] || r[\"@@iterator\"]; if (null != t) { var e, n, i, u, a = [], f = !0, o = !1; try { if (i = (t = t.call(r)).next, 0 === l) { if (Object(t) !== t) return; f = !1; } else for (; !(f = (e = i.call(t)).done) && (a.push(e.value), a.length !== l); f = !0); } catch (r) { o = !0, n = r; } finally { try { if (!f && null != t.return && (u = t.return(), Object(u) !== u)) return; } finally { if (o) throw n; } } return a; } }\nfunction _arrayWithHoles(r) { if (Array.isArray(r)) return r; }\nvar __awaiter = undefined && undefined.__awaiter || function (thisArg, _arguments, P, generator) {\n  function adopt(value) {\n    return value instanceof P ? value : new P(function (resolve) {\n      resolve(value);\n    });\n  }\n  return new (P || (P = Promise))(function (resolve, reject) {\n    function fulfilled(value) {\n      try {\n        step(generator.next(value));\n      } catch (e) {\n        reject(e);\n      }\n    }\n    function rejected(value) {\n      try {\n        step(generator[\"throw\"](value));\n      } catch (e) {\n        reject(e);\n      }\n    }\n    function step(result) {\n      result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected);\n    }\n    step((generator = generator.apply(thisArg, _arguments || [])).next());\n  });\n};\n\n\n\n\n/** Presentation chỉ render DTO và gọi callback; không đọc context hoặc dataset trực tiếp. */\nfunction PointGrid(props) {\n  var _React$useState = react__WEBPACK_IMPORTED_MODULE_0__.useState(),\n    _React$useState2 = _slicedToArray(_React$useState, 2),\n    actionError = _React$useState2[0],\n    setActionError = _React$useState2[1];\n  var openPoint = id => __awaiter(this, void 0, void 0, function* () {\n    setActionError(undefined);\n    try {\n      yield props.onOpenPoint(id);\n    } catch (_a) {\n      // Không hiển thị lỗi kỹ thuật/credential từ host cho người dùng cuối.\n      setActionError(props.text.errorPrefix);\n    }\n  });\n  var hasRows = props.model.rows.length > 0;\n  return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"section\", {\n    className: \"fmc-point-grid\",\n    \"aria-label\": props.text.title\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"header\", {\n    className: \"fmc-point-grid__header\"\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"h2\", {\n    className: \"fmc-point-grid__title\"\n  }, props.text.title), props.model.totalResultCount !== undefined && /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", {\n    className: \"fmc-point-grid__count\"\n  }, props.text.total(props.model.totalResultCount))), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Button, {\n    appearance: \"secondary\",\n    onClick: props.onRefresh,\n    disabled: props.model.isLoading\n  }, props.text.refresh)), actionError && /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: \"fmc-point-grid__state fmc-point-grid__state--error\",\n    role: \"alert\"\n  }, actionError), !hasRows ? /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_GridState__WEBPACK_IMPORTED_MODULE_2__.GridState, {\n    isLoading: props.model.isLoading,\n    errorMessage: props.model.errorMessage,\n    loadingText: props.text.loading,\n    emptyText: props.text.empty,\n    errorPrefix: props.text.errorPrefix\n  }) : /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"table\", {\n    className: \"fmc-point-grid__table\"\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"thead\", null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"tr\", null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"th\", {\n    scope: \"col\"\n  }, props.text.point), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"th\", {\n    scope: \"col\"\n  }, props.text.objectId), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"th\", {\n    scope: \"col\"\n  }, props.text.currentValue), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"th\", {\n    scope: \"col\"\n  }, props.text.readingTime), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"th\", {\n    scope: \"col\"\n  }, props.text.source))), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"tbody\", null, props.model.rows.map(row => {\n    var _a, _b;\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"tr\", {\n      key: row.id\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"th\", {\n      scope: \"row\"\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Button, {\n      appearance: \"transparent\",\n      onClick: () => {\n        void openPoint(row.id);\n      },\n      \"aria-label\": props.text.rowAction(row.name)\n    }, row.name)), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"td\", null, row.objectId), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"td\", null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_ReadingValue__WEBPACK_IMPORTED_MODULE_3__.ReadingValue, {\n      value: row.currentValue,\n      unit: row.unit\n    })), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"td\", null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"time\", {\n      dateTime: (_a = row.lastReadingTimeUtc) === null || _a === void 0 ? void 0 : _a.toISOString()\n    }, formatReadingTime(row.lastReadingTimeUtc)), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", {\n      className: \"fmc-point-grid__freshness fmc-point-grid__freshness--\".concat(row.freshness)\n    }, props.text.freshness[row.freshness])), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"td\", null, (_b = row.sourceSystem) !== null && _b !== void 0 ? _b : \"—\"));\n  }))), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"footer\", {\n    className: \"fmc-point-grid__pager\",\n    \"aria-label\": \"Paging\"\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Button, {\n    appearance: \"secondary\",\n    onClick: props.onPreviousPage,\n    disabled: props.model.isLoading || !props.model.hasPreviousPage\n  }, props.text.previous), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Button, {\n    appearance: \"secondary\",\n    onClick: props.onNextPage,\n    disabled: props.model.isLoading || !props.model.hasNextPage\n  }, props.text.next)));\n}\n/** Format chỉ để đọc trên browser; Date gốc vẫn được giữ UTC trong DTO. */\nfunction formatReadingTime(value) {\n  if (!value || Number.isNaN(value.getTime())) {\n    return \"—\";\n  }\n  return new Intl.DateTimeFormat(undefined, {\n    dateStyle: \"short\",\n    timeStyle: \"medium\"\n  }).format(value);\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/Presentation/Components/PointGrid.tsx?\n}");

/***/ },

/***/ "./BmsPointGrid/Presentation/Components/ReadingValue.tsx"
/*!***************************************************************!*\
  !*** ./BmsPointGrid/Presentation/Components/ReadingValue.tsx ***!
  \***************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   ReadingValue: () => (/* binding */ ReadingValue)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n\n/** Hiển thị số đến bốn chữ số thập phân mà không làm tròn/ghi lại dữ liệu nguồn. */\nfunction ReadingValue(props) {\n  if (props.value === undefined) {\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", null, \"\\u2014\");\n  }\n  var formatted = new Intl.NumberFormat(undefined, {\n    maximumFractionDigits: 4\n  }).format(props.value);\n  return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", null, props.unit ? \"\".concat(formatted, \" \").concat(props.unit) : formatted);\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/Presentation/Components/ReadingValue.tsx?\n}");

/***/ },

/***/ "./BmsPointGrid/index.ts"
/*!*******************************!*\
  !*** ./BmsPointGrid/index.ts ***!
  \*******************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   BmsPointGrid: () => (/* binding */ BmsPointGrid)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n/* harmony import */ var _Business_Services_PointGridService__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! ./Business/Services/PointGridService */ \"./BmsPointGrid/Business/Services/PointGridService.ts\");\n/* harmony import */ var _DataAccess_Adapters_PcfPointDataSetAdapter__WEBPACK_IMPORTED_MODULE_2__ = __webpack_require__(/*! ./DataAccess/Adapters/PcfPointDataSetAdapter */ \"./BmsPointGrid/DataAccess/Adapters/PcfPointDataSetAdapter.ts\");\n/* harmony import */ var _DataAccess_Adapters_PcfPointNavigationAdapter__WEBPACK_IMPORTED_MODULE_3__ = __webpack_require__(/*! ./DataAccess/Adapters/PcfPointNavigationAdapter */ \"./BmsPointGrid/DataAccess/Adapters/PcfPointNavigationAdapter.ts\");\n/* harmony import */ var _Common_Localization_messages__WEBPACK_IMPORTED_MODULE_4__ = __webpack_require__(/*! ./Common/Localization/messages */ \"./BmsPointGrid/Common/Localization/messages.ts\");\n/* harmony import */ var _Presentation_Components_PointGrid__WEBPACK_IMPORTED_MODULE_5__ = __webpack_require__(/*! ./Presentation/Components/PointGrid */ \"./BmsPointGrid/Presentation/Components/PointGrid.tsx\");\n\n\n\n\n\n\n/** Số phút mặc định trước khi UI gắn nhãn dữ liệu cũ. */\nvar DEFAULT_STALE_AFTER_MINUTES = 30;\nclass BmsPointGrid {\n  constructor() {\n    /** Adapter giữ ranh giới giữa API Power Apps và code nghiệp vụ thuần. */\n    this.pointDataSource = new _DataAccess_Adapters_PcfPointDataSetAdapter__WEBPACK_IMPORTED_MODULE_2__.PcfPointDataSetAdapter();\n    /** Service chuyển dữ liệu Point thành DTO mà React cần để hiển thị. */\n    this.pointGridService = new _Business_Services_PointGridService__WEBPACK_IMPORTED_MODULE_1__.PointGridService();\n  }\n  /**\n   * Used to initialize the control instance. Controls can kick off remote server calls and other initialization actions here.\n   * Data-set values are not initialized here, use updateView.\n   * @param context The entire property bag available to control via Context Object; It contains values as set up by the customizer mapped to property names defined in the manifest, as well as utility functions.\n   * @param notifyOutputChanged A callback method to alert the framework that the control has new outputs ready to be retrieved asynchronously.\n   * @param state A piece of data that persists in one session for a single user. Can be set at any point in a controls life cycle by calling 'setControlState' in the Mode interface.\n   */\n  init(context, _notifyOutputChanged, _state) {\n    // MVP chỉ đọc nên không có output; đặt dấu _ để nói rõ hai tham số này chưa cần dùng.\n    this.navigation = new _DataAccess_Adapters_PcfPointNavigationAdapter__WEBPACK_IMPORTED_MODULE_3__.PcfPointNavigationAdapter(context.navigation);\n  }\n  /**\n   * Called when any value in the property bag has changed. This includes field values, data-sets, global values such as container height and width, offline status, control metadata values such as label, visible, etc.\n   * @param context The entire property bag available to control via Context Object; It contains values as set up by the customizer mapped to names defined in the manifest, as well as utility functions\n   * @returns ReactElement root react element for the control\n   */\n  updateView(context) {\n    var _a, _b;\n    // updateView là điểm Power Apps đưa dataset mới vào; không tự gọi refresh tại đây để tránh render loop.\n    this.pointDataSource.update(context.parameters.points);\n    (_a = this.navigation) === null || _a === void 0 ? void 0 : _a.update(context.navigation);\n    var staleAfterMinutes = (_b = context.parameters.staleAfterMinutes.raw) !== null && _b !== void 0 ? _b : DEFAULT_STALE_AFTER_MINUTES;\n    var viewModel = this.pointGridService.createViewModel(this.pointDataSource.getSnapshot(), {\n      now: new Date(),\n      staleAfterMinutes\n    });\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_Presentation_Components_PointGrid__WEBPACK_IMPORTED_MODULE_5__.PointGrid, {\n      model: viewModel,\n      text: (0,_Common_Localization_messages__WEBPACK_IMPORTED_MODULE_4__.getText)(context.userSettings.languageId),\n      onRefresh: () => this.pointDataSource.refresh(),\n      onNextPage: () => this.pointDataSource.loadNextPage(),\n      onPreviousPage: () => this.pointDataSource.loadPreviousPage(),\n      onOpenPoint: id => {\n        var _a, _b;\n        return (_b = (_a = this.navigation) === null || _a === void 0 ? void 0 : _a.openPoint(id)) !== null && _b !== void 0 ? _b : Promise.resolve();\n      }\n    });\n  }\n  /**\n   * It is called by the framework prior to a control receiving new data.\n   * @returns an object based on nomenclature defined in manifest, expecting object[s] for property marked as \"bound\" or \"output\"\n   */\n  getOutputs() {\n    return {};\n  }\n  /**\n   * Called when the control is to be removed from the DOM tree. Controls should use this call for cleanup.\n   * i.e. cancelling any pending remote calls, removing listeners, etc.\n   */\n  destroy() {\n    // Adapter không tạo timer/listener; đặt undefined để instance không giữ context cũ sau khi control bị tháo.\n    this.navigation = undefined;\n  }\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./BmsPointGrid/index.ts?\n}");

/***/ },

/***/ "@fluentui/react-components"
/*!************************************!*\
  !*** external "FluentUIReactv940" ***!
  \************************************/
(module) {

module.exports = FluentUIReactv940;

/***/ },

/***/ "react"
/*!***************************!*\
  !*** external "Reactv16" ***!
  \***************************/
(module) {

module.exports = Reactv16;

/***/ }

/******/ 	});
/************************************************************************/
/******/ 	// The module cache
/******/ 	const __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		const cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		const module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		if (!(moduleId in __webpack_modules__)) {
/******/ 			delete __webpack_module_cache__[moduleId];
/******/ 			const e = new Error("Cannot find module '" + moduleId + "'");
/******/ 			e.code = 'MODULE_NOT_FOUND';
/******/ 			throw e;
/******/ 		}
/******/ 		__webpack_modules__[moduleId](module, module.exports, __webpack_require__);
/******/ 	
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/ 	
/************************************************************************/
/******/ 	/* webpack/runtime/compat get default export */
/******/ 	// getDefaultExport function for compatibility with non-harmony modules
/******/ 	__webpack_require__.n = (module) => {
/******/ 		const getter = module && module.__esModule ?
/******/ 			() => (module['default']) :
/******/ 			() => (module);
/******/ 		__webpack_require__.d(getter, { a: getter });
/******/ 		return getter;
/******/ 	};
/******/ 	
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
/******/ 	let __webpack_exports__ = __webpack_require__("./BmsPointGrid/index.ts");
/******/ 	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = __webpack_exports__;
/******/ 	
/******/ })()
;
if (window.ComponentFramework && window.ComponentFramework.registerControl) {
	ComponentFramework.registerControl('FMC.Bms.BmsPointGrid', pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.BmsPointGrid);
} else {
	var FMC = FMC || {};
	FMC.Bms = FMC.Bms || {};
	FMC.Bms.BmsPointGrid = pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.BmsPointGrid;
	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = undefined;
}