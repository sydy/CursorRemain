import unittest

from cursor_api import (
    USAGE_URL,
    BILLING_URL,
    apply_sand_usage_status,
    dashboard_button_label,
    dashboard_link_label,
    dashboard_menu_label,
    dashboard_url_for,
    format_membership_type,
    format_spend_range,
    format_usd_cents,
    parse_usage_summary,
)


PERSONAL_ULTRA = {
    "billingCycleStart": "2026-07-04T00:35:51.000Z",
    "billingCycleEnd": "2026-08-04T00:35:51.000Z",
    "membershipType": "ultra",
    "limitType": "user",
    "isUnlimited": False,
    "autoModelSelectedDisplayMessage": "You've used 98% of your included total usage",
    "namedModelSelectedDisplayMessage": "You've used 100% of your included API usage",
    "individualUsage": {
        "plan": {
            "enabled": True,
            "used": 40000,
            "limit": 40000,
            "remaining": 0,
            "autoPercentUsed": 98.109,
            "apiPercentUsed": 100,
            "totalPercentUsed": 98.5128,
        },
        "onDemand": {"enabled": False, "used": 0, "limit": None, "remaining": None},
    },
    "teamUsage": {},
}

ENTERPRISE_OVERALL = {
    "billingCycleStart": "2026-07-01T00:00:00.000Z",
    "billingCycleEnd": "2026-08-01T00:00:00.000Z",
    "membershipType": "enterprise",
    "limitType": "team",
    "isUnlimited": False,
    "individualUsage": {
        "overall": {"enabled": True, "used": 7384, "limit": 10000, "remaining": 2616}
    },
    "teamUsage": {
        "onDemand": {"enabled": True, "used": 0, "limit": None, "remaining": None},
        "pooled": {"enabled": True, "used": 12725135, "limit": 28122000, "remaining": 15396865},
    },
}

ENTERPRISE_PLAN_PERCENT = {
    "billingCycleStart": "2026-03-01T00:00:00.000Z",
    "billingCycleEnd": "2026-04-01T00:00:00.000Z",
    "membershipType": "enterprise",
    "limitType": "team",
    "isUnlimited": False,
    "autoModelSelectedDisplayMessage": "You've used 7% of your included total usage",
    "namedModelSelectedDisplayMessage": "You've used 7% of your included API usage",
    "individualUsage": {
        "plan": {
            "enabled": True,
            "used": 0,
            "limit": 0,
            "remaining": 0,
            "breakdown": {"included": 0, "bonus": 300, "total": 300},
            "autoPercentUsed": 0,
            "apiPercentUsed": 6.9,
            "totalPercentUsed": 6.9,
        },
        "onDemand": {"enabled": False, "used": 0, "limit": 0, "remaining": 0},
    },
    "teamUsage": {
        "onDemand": {"enabled": True, "used": 0, "limit": 10000, "remaining": 10000}
    },
}

ENTERPRISE_DISPLAY_ONLY = {
    "billingCycleEnd": "2026-08-04T00:35:51.000Z",
    "membershipType": "team",
    "isUnlimited": False,
    "autoModelSelectedDisplayMessage": "You've used 42% of your included total usage",
    "namedModelSelectedDisplayMessage": "You've used 15% of your included API usage",
    "teamUsage": {"onDemand": {"enabled": True}},
}

ENTERPRISE_UNLIMITED = {
    "billingCycleEnd": "2026-08-04T00:00:00Z",
    "membershipType": "enterprise",
    "isUnlimited": True,
}

TEAM_OVERALL_STALE_ZERO = {
    "billingCycleStart": "2026-08-19T00:00:00.000Z",
    "billingCycleEnd": "2026-09-19T00:00:00.000Z",
    "membershipType": "team",
    "limitType": "team",
    "isUnlimited": False,
    "individualUsage": {
        "plan": {
            "enabled": True,
            "used": 0,
            "limit": 0,
            "remaining": 0,
            "autoPercentUsed": 0,
            "apiPercentUsed": 0,
            "totalPercentUsed": 0,
        },
        "overall": {"enabled": True, "used": 7180, "limit": 100000, "remaining": 92820},
        "onDemand": {"enabled": False, "used": 0, "limit": None, "remaining": None},
    },
    "teamUsage": {"onDemand": {"enabled": True, "used": 0, "limit": None, "remaining": None}},
}

TEAM_OVERALL_STALE_FULL = {
    "billingCycleStart": "2026-08-19T00:00:00.000Z",
    "billingCycleEnd": "2026-09-19T00:00:00.000Z",
    "membershipType": "team",
    "limitType": "team",
    "isUnlimited": False,
    "individualUsage": {
        "plan": {
            "enabled": True,
            "used": 0,
            "limit": 0,
            "remaining": 0,
            "autoPercentUsed": 100,
            "apiPercentUsed": 100,
            "totalPercentUsed": 100,
        },
        "overall": {"enabled": True, "used": 7180, "limit": 100000, "remaining": 92820},
    },
    "teamUsage": {},
}


class ParseUsageSummaryTests(unittest.TestCase):
    def test_personal_percent_plan_unchanged(self) -> None:
        snap = parse_usage_summary(PERSONAL_ULTRA)
        self.assertEqual(snap.membership_type, "Ultra")
        self.assertEqual(snap.billing_mode, "percent")
        self.assertEqual(snap.used_percent, 98.5)
        self.assertEqual(snap.remaining_percent, 1.5)
        self.assertEqual(snap.auto_percent_used, 98.1)
        self.assertEqual(snap.api_percent_used, 100.0)
        self.assertFalse(snap.is_team_account())
        self.assertFalse(snap.shows_amount())
        self.assertEqual(dashboard_url_for(snap), BILLING_URL)
        self.assertEqual(dashboard_button_label(snap), "账单")
        self.assertEqual(dashboard_menu_label(snap), "打开用量账单")
        self.assertEqual(dashboard_link_label(snap), "查看用量账单 →")

    def test_enterprise_overall_amount_billing(self) -> None:
        snap = parse_usage_summary(ENTERPRISE_OVERALL)
        self.assertEqual(snap.membership_type, "Enterprise")
        self.assertEqual(snap.billing_mode, "amount")
        self.assertTrue(snap.is_team_account())
        self.assertTrue(snap.shows_amount())
        self.assertEqual(snap.used_percent, 73.8)
        self.assertEqual(snap.remaining_percent, 26.2)
        self.assertEqual(snap.used_cents, 7384)
        self.assertEqual(snap.limit_cents, 10000)
        self.assertEqual(snap.pooled_used_cents, 12725135)
        self.assertEqual(snap.pooled_limit_cents, 28122000)
        self.assertEqual(dashboard_url_for(snap), USAGE_URL)
        self.assertEqual(dashboard_button_label(snap), "用量")
        self.assertEqual(dashboard_menu_label(snap), "打开用量")
        self.assertEqual(dashboard_link_label(snap), "查看用量 →")
        self.assertEqual(format_spend_range(snap.used_cents, snap.limit_cents), "$73.84 / $100")

    def test_enterprise_plan_percent_keeps_included_usage(self) -> None:
        snap = parse_usage_summary(ENTERPRISE_PLAN_PERCENT)
        self.assertEqual(snap.billing_mode, "percent")
        self.assertEqual(snap.used_percent, 6.9)
        self.assertEqual(snap.remaining_percent, 93.1)
        self.assertEqual(snap.api_percent_used, 6.9)
        self.assertTrue(snap.is_team_account())
        self.assertFalse(snap.shows_amount())
        self.assertEqual(snap.on_demand_used_cents, 0)
        self.assertEqual(snap.on_demand_limit_cents, 10000)
        self.assertEqual(dashboard_url_for(snap), USAGE_URL)

    def test_team_display_message_fallback(self) -> None:
        snap = parse_usage_summary(ENTERPRISE_DISPLAY_ONLY)
        self.assertEqual(snap.membership_type, "Team")
        self.assertEqual(snap.auto_percent_used, 42.0)
        self.assertEqual(snap.api_percent_used, 15.0)
        self.assertEqual(snap.used_percent, 42.0)
        self.assertEqual(snap.remaining_percent, 58.0)
        self.assertTrue(snap.is_team_account())
        self.assertEqual(dashboard_url_for(snap), USAGE_URL)

    def test_enterprise_pooled_only_fallback(self) -> None:
        snap = parse_usage_summary(
            {
                "membershipType": "enterprise",
                "limitType": "team",
                "teamUsage": {
                    "pooled": {
                        "enabled": True,
                        "used": 3479810,
                        "limit": 60000000,
                        "remaining": 56520190,
                    }
                },
            }
        )
        self.assertEqual(snap.billing_mode, "amount")
        self.assertEqual(snap.used_percent, 5.8)
        self.assertEqual(snap.remaining_percent, 94.2)
        self.assertEqual(snap.used_cents, 3479810)
        self.assertEqual(snap.limit_cents, 60000000)
        self.assertTrue(snap.shows_amount())
        self.assertEqual(dashboard_url_for(snap), USAGE_URL)

    def test_unlimited_enterprise(self) -> None:
        snap = parse_usage_summary(ENTERPRISE_UNLIMITED)
        self.assertTrue(snap.is_unlimited)
        self.assertEqual(snap.used_percent, 0.0)
        self.assertEqual(snap.remaining_percent, 100.0)
        self.assertTrue(snap.is_team_account())
        self.assertEqual(dashboard_url_for(snap), USAGE_URL)

    def test_legacy_plan_used_limit_ratio(self) -> None:
        snap = parse_usage_summary(
            {
                "membershipType": "pro",
                "individualUsage": {"plan": {"used": 25, "limit": 100}},
            }
        )
        self.assertEqual(snap.billing_mode, "percent")
        self.assertEqual(snap.used_percent, 25.0)
        self.assertEqual(snap.remaining_percent, 75.0)
        self.assertIsNone(snap.used_cents)
        self.assertFalse(snap.shows_amount())

    def test_team_overall_spend_beats_stale_plan_percent(self) -> None:
        snap = parse_usage_summary(TEAM_OVERALL_STALE_ZERO)
        self.assertEqual(snap.billing_mode, "amount")
        self.assertEqual(snap.used_percent, 7.2)
        self.assertEqual(snap.remaining_percent, 92.8)
        self.assertEqual(snap.used_cents, 7180)
        self.assertEqual(snap.limit_cents, 100000)
        self.assertEqual(snap.total_percent_used, 0.0)
        self.assertTrue(snap.shows_amount())
        self.assertEqual(format_spend_range(snap.used_cents, snap.limit_cents), "$71.80 / $1000")

        capped = parse_usage_summary(TEAM_OVERALL_STALE_FULL)
        self.assertEqual(capped.used_percent, 7.2)
        self.assertEqual(capped.remaining_percent, 92.8)
        self.assertEqual(capped.total_percent_used, 100.0)
        self.assertEqual(capped.auto_percent_used, 100.0)


class FetchUsageSummaryTests(unittest.TestCase):
    def test_detail_failures_are_logged_without_breaking_plan(self) -> None:
        from unittest.mock import patch

        from cursor_api import CursorApiError, fetch_usage_summary

        logs: list[str] = []

        def logger(msg: str, *args: object, **_kwargs: object) -> None:
            logs.append(msg % args if args else msg)

        def fake_request(method, endpoint, token, body=None, timeout=30.0):
            if endpoint in ("/api/usage-summary", "/api/dashboard/usage-summary"):
                return PERSONAL_ULTRA
            raise CursorApiError("明细挂了", status_code=500)

        with patch("cursor_api._request_json", side_effect=fake_request):
            snap = fetch_usage_summary("user_01LOG%3A%3Afake.jwt.sig", logger=logger)
        self.assertEqual(snap.used_percent, 98.5)
        self.assertEqual(snap.remaining_percent, 1.5)
        self.assertEqual(snap.model_usages, ())
        self.assertIsNone(snap.grok_bot_percent_used)
        self.assertEqual(len(logs), 2)
        self.assertIn("attach_aggregated_tokens failed", logs[0])
        self.assertIn("明细挂了", logs[0])
        self.assertIn("attach_grok_bot_usage failed", logs[1])
        self.assertIn("明细挂了", logs[1])


class FormatHelpersTests(unittest.TestCase):
    def test_membership_and_money(self) -> None:
        self.assertEqual(format_membership_type("enterprise"), "Enterprise")
        self.assertEqual(format_membership_type("pro_plus"), "Pro+")
        self.assertEqual(format_usd_cents(7384), "$73.84")
        self.assertEqual(format_usd_cents(10000), "$100")
        self.assertEqual(format_usd_cents(0), "$0")
        self.assertEqual(format_spend_range(71, 10000), "$0.71 / $100")

    def test_dashboard_url_from_membership_only(self) -> None:
        self.assertEqual(dashboard_url_for(membership="enterprise"), USAGE_URL)
        self.assertEqual(dashboard_url_for(membership="Pro"), BILLING_URL)
        self.assertEqual(dashboard_url_for(membership="ultra", limit_type="team"), USAGE_URL)


class StatusTextEnterpriseTests(unittest.TestCase):
    def test_amount_rows(self) -> None:
        from status_text import build_status_lines

        snap = parse_usage_summary(ENTERPRISE_OVERALL)
        rows = dict(build_status_lines(snap, None, "12:00"))
        self.assertIn("$73.84 / $100", rows["剩余"])
        self.assertEqual(rows["金额"], "$73.84 / $100")
        self.assertIn("$", rows["团队额度"])
        self.assertEqual(rows["计划"], "Enterprise")

    def test_stale_plan_percent_does_not_override_monthly_spend(self) -> None:
        from status_text import build_status_lines

        snap = parse_usage_summary(TEAM_OVERALL_STALE_ZERO)
        rows = dict(build_status_lines(snap, None, "12:00"))
        self.assertIn("92.8%", rows["剩余"])
        self.assertIn("$71.80 / $1000", rows["剩余"])
        self.assertEqual(rows["金额"], "$71.80 / $1000")


class SourceGuardTests(unittest.TestCase):
    def test_native_usage_report_entry_points(self) -> None:
        from pathlib import Path

        root = Path(__file__).resolve().parents[1]
        win_prog = (root / "windows" / "CursorRemain" / "Program.cs").read_text(encoding="utf-8")
        win_parser = (root / "windows" / "CursorTokenCore" / "UsageParser.cs").read_text(encoding="utf-8")
        win_report = (root / "windows" / "CursorRemain" / "ReportForm.cs").read_text(encoding="utf-8")
        win_chart = (root / "windows" / "CursorRemain" / "UsageChartPanel.cs").read_text(encoding="utf-8")
        mac_menu = (root / "macos" / "Sources" / "CursorRemain" / "StatusItemController.swift").read_text(encoding="utf-8")
        mac_parser = (root / "macos" / "Sources" / "CursorTokenCore" / "UsageParser.swift").read_text(encoding="utf-8")
        mac_report = (root / "macos" / "Sources" / "CursorRemain" / "ReportView.swift").read_text(encoding="utf-8")
        mac_chart = (root / "macos" / "Sources" / "CursorRemain" / "UsageChartView.swift").read_text(encoding="utf-8")
        self.assertIn("用量报表", win_prog)
        self.assertIn("OpenReport", win_prog)
        self.assertIn("--report", win_prog)
        self.assertIn("--settings", win_prog)
        self.assertIn("RequestOpenSettings", win_prog)
        self.assertIn("TrayIpc", win_prog)
        self.assertIn("RequestOpenReport", win_prog)
        self.assertIn("账号对比", win_prog)
        self.assertIn("OpenCompare", win_prog)
        self.assertIn("LoginToCursor", win_prog)
        self.assertIn("在 Cursor 登录当前账号", win_prog)
        self.assertIn("loginCursor", mac_menu)
        self.assertIn("在 Cursor 登录当前账号", mac_menu)
        win_auth = (root / "windows" / "CursorTokenCore" / "CursorAuth.cs").read_text(encoding="utf-8")
        mac_auth = (root / "macos" / "Sources" / "CursorTokenCore" / "CursorAuth.swift").read_text(encoding="utf-8")
        for src in (win_auth, mac_auth):
            self.assertIn("glass.lastSignedInAuthId", src)
            self.assertIn("cursorAuth/accessToken", src)
            self.assertNotIn("cursorAuth/onboardingDate", src)
            self.assertNotIn("telemetry.machineId", src)
        self.assertIn("get-filtered-usage-events", win_parser)
        self.assertIn("get-sand-usage-status", win_parser)
        self.assertIn("ParseSandUsageStatus", win_parser)
        win_layout = (root / "windows" / "CursorTokenCore" / "UiLayout.cs").read_text(encoding="utf-8")
        self.assertIn("UsageChartPanel", win_report)
        self.assertIn("LegendStrip", win_chart)
        self.assertIn("ScalePx(8", win_chart)
        self.assertIn("RenderAsync", win_report)
        self.assertIn("ApplyDetails", win_report)
        self.assertIn("PaintOnce", win_report)
        self.assertIn("按小时", win_chart)
        self.assertIn("BuildChart", win_chart)
        self.assertIn("SegmentedToggle", win_chart)
        self.assertIn("GrowAndShrink", win_chart)
        self.assertIn("public const int DesignToggleW = 150", win_layout)
        self.assertIn("WrapChips", win_layout)
        self.assertIn(".frame(width: 150)", mac_chart)
        self.assertIn("strokeBorder", mac_chart)
        self.assertIn("ChipFlowLayout", mac_chart)
        self.assertIn("wrapChips", mac_chart)
        self.assertNotIn("GridItem(.adaptive", mac_chart)
        mac_chart_layout = (root / "macos" / "Sources" / "CursorTokenCore" / "UsageChartLayout.swift").read_text(encoding="utf-8")
        self.assertIn("legendChipHSpacing = 8", mac_chart_layout)
        self.assertIn("func wrapChips", mac_chart_layout)
        self.assertNotIn("SparklineBox", win_report)
        self.assertIn("用量报表", mac_menu)
        self.assertIn("openReport", mac_menu)
        self.assertIn("账号对比", mac_menu)
        self.assertIn("openCompare", mac_menu)
        self.assertIn("formatAccountMenuTitle", mac_menu)
        self.assertIn("FormatAccountMenuTitle", win_prog)
        self.assertIn("get-filtered-usage-events", mac_parser)
        self.assertIn("get-sand-usage-status", mac_parser)
        self.assertIn("parseSandUsageStatus", mac_parser)
        self.assertIn("UsageChartView", mac_report)
        self.assertIn("layoutPriority(-1)", mac_report)
        self.assertIn("maxHeight: 280", mac_report)
        self.assertIn("chartHourly", mac_report)
        self.assertIn("全部额度", win_report)
        self.assertIn("First-party", win_report)
        self.assertIn("Grok Bot", win_report)
        self.assertIn("开始日期", win_report)
        self.assertIn("结束日期", win_report)
        self.assertLess(win_report.index("开始日期"), win_report.index('FilterTag("类型"'))
        self.assertLess(mac_report.index("开始日期"), mac_report.index('Picker("类型"'))
        self.assertIn("本地时间", win_report)
        self.assertIn("本地时间", mac_report)
        self.assertNotIn("北京时间", win_report)
        self.assertNotIn("北京时间", mac_report)
        self.assertNotIn("北京时间", win_chart)
        self.assertNotIn("北京时间", mac_chart)
        self.assertIn("ShowCheckBox", win_report)
        self.assertIn("ReportStartDate", win_report)
        self.assertIn("SetReportRange", win_prog)
        self.assertIn("CategoryFirstParty", win_report)
        self.assertIn("企业额度（展示）", win_report)
        self.assertIn("折扣", win_report)
        self.assertIn("每百万Token", win_report)
        self.assertIn("FormatReportPerMillionKpi", win_report)
        self.assertIn("UsesActualCny", win_report)
        self.assertIn("FormatReportSpendKpi", win_report)
        self.assertNotIn("预计实付", win_report)
        win_settings = (root / "windows" / "CursorRemain" / "UiForms.cs").read_text(encoding="utf-8")
        mac_settings = (root / "macos" / "Sources" / "CursorRemain" / "SettingsView.swift").read_text(encoding="utf-8")
        self.assertIn("FormatSyncStatus", win_settings)
        self.assertIn("formatSyncStatus", mac_settings)
        for src in (win_settings, mac_settings):
            self.assertIn("实际成本（人民币）", src)
            self.assertIn("额度不是真实支出", src)
            self.assertIn("仅当前账号", src)
            self.assertIn("折合月费", src)
            self.assertIn("自费", src)
            self.assertIn("第三方", src)
            self.assertIn("按需不再按官网标价另加", src)
            self.assertNotIn("按需仍按费用×汇率", src)
            self.assertIn("其他设备用新密码重新登录", src)
        win_compare = (root / "windows" / "CursorRemain" / "CompareForm.cs").read_text(encoding="utf-8")
        mac_compare = (root / "macos" / "Sources" / "CursorRemain" / "CompareView.swift").read_text(encoding="utf-8")
        for src in (win_compare, mac_compare):
            self.assertIn("账号对比", src)
            self.assertIn("日均持有", src)
            self.assertIn("First-party", src)
            self.assertIn("Grok Bot", src)
        self.assertIn("CompareHint", win_compare)
        self.assertIn("formatCompareHint", mac_compare)
        self.assertIn("CompareBestEligible", win_compare)
        self.assertIn("compareBestEligible", mac_compare)
        self.assertIn("CompareRowNote", win_compare)
        self.assertIn("compareRowNote", mac_compare)
        self.assertIn("FormatCompareHint", win_compare)
        self.assertIn("formatCompareHint", mac_compare)
        win_chrome = (root / "windows" / "CursorRemain" / "UiChrome.cs").read_text(encoding="utf-8")
        win_tone = (root / "windows" / "CursorTokenCore" / "FormTone.cs").read_text(encoding="utf-8")
        self.assertIn("class UiChrome", win_chrome)
        self.assertIn("FlatStyle.Flat", win_chrome)
        self.assertIn("EnableHeadersVisualStyles = false", win_chrome)
        self.assertIn("UiButtonKind.Primary", win_chrome)
        self.assertIn("class HintBlock", win_chrome)
        self.assertIn("class KpiStrip", win_chrome)
        self.assertIn("class FlatTabControl", win_chrome)
        self.assertIn("class DarkMenuRenderer", win_chrome)
        self.assertIn("ScrollBars.None", win_settings)
        self.assertIn("TabSizeMode.Fixed", win_chrome)
        self.assertIn("ButtonMinWidth", win_chrome)
        self.assertIn("SizeToText", win_chrome)
        self.assertIn("AutoSize = false", win_chrome)
        self.assertIn("LabelColumn", win_settings)
        self.assertIn("StyleMenu", win_settings)
        self.assertIn("TabItemWidth", win_tone)
        self.assertIn("class FieldFrame", win_chrome)
        self.assertIn("public static FieldFrame Frame", win_chrome)
        self.assertIn("DwmwaUseImmersiveDarkMode", win_chrome)
        self.assertIn("了解更多", win_chrome)
        self.assertIn("Segoe UI Variable", win_chrome)
        self.assertIn("public static Label Heading", win_chrome)
        self.assertIn("public static class FormTone", win_tone)
        self.assertIn("PromptDialog", win_settings)
        self.assertIn("其他导入方式", win_settings)
        self.assertIn("成本与渠道", win_settings)
        self.assertIn("成本与渠道", mac_settings)
        self.assertIn("从 Cursor 导入", win_settings)
        self.assertIn("FlatTabControl", win_settings)
        self.assertIn("Height = 88", win_settings)
        self.assertIn("UiChrome.Frame(_token", win_settings)
        self.assertIn("tokenFrame.Padding", win_settings)
        self.assertIn("KpiStrip", win_report)
        self.assertIn("FilterTag", win_report)
        self.assertIn("LayoutToolbar", win_report)
        self.assertIn("_summaryBar", win_report)
        self.assertIn("remain >=", win_report)
        self.assertIn("_mixHost", win_report)
        self.assertIn("KpiValueBottom", win_report)
        self.assertIn("BottomLeft", win_report)
        self.assertIn("FillMixText", win_report)
        self.assertIn("lastTextBaseline", mac_report)
        self.assertIn("ViewThatFits", mac_report)
        self.assertIn("mixBlock", mac_report)
        self.assertIn("class WrapBar", win_report)
        self.assertIn("GetPreferredSize", win_report)
        self.assertIn("WrapContents = true", win_report)
        self.assertIn("ChipFlowLayout", mac_report)
        self.assertIn("FillMixText", win_report)
        self.assertIn("ApplyColumnSizing", win_report)
        self.assertIn("FitFill", win_report)
        self.assertIn("HeaderCell.Style.Alignment", win_report)
        self.assertNotIn("_pad", win_report)
        self.assertIn("VirtualMode", win_report)
        self.assertIn("CellValueNeeded", win_report)
        self.assertIn("Task.Run", win_report)
        self.assertIn("DetailCost", win_report)
        self.assertIn("SpreadTo", win_chrome)
        self.assertIn("MiddleRight", win_report)
        self.assertIn("MiddleRight", win_compare)
        self.assertIn("public const int PagePadding = 20", win_layout)
        self.assertIn("UiChrome.Install", win_prog)
        self.assertIn("UiChrome.Apply", win_settings)
        self.assertIn("UiChrome.Apply", win_report)
        self.assertIn("UiChrome.Apply", win_compare)
        self.assertIn("FormTone.For", (root / "windows" / "CursorRemain" / "FlyoutForm.cs").read_text(encoding="utf-8"))
        self.assertNotIn("BackgroundColor = Color.White", win_report)
        self.assertNotIn("BackgroundColor = Color.White", win_compare)
        self.assertNotIn("g.Clear(Color.White)", win_chart)
        self.assertIn("UiChrome.Tone.Window", win_chart)
        self.assertIn("SelectionFill", win_compare)
        self.assertIn("ActionBar", win_chrome)
        self.assertIn("SetRedraw", win_chrome)
        self.assertIn("Equalize", win_chrome)
        self.assertIn("ActionBar", win_compare)
        self.assertIn("FormWindowState.Maximized", win_report)
        self.assertIn("SplitContainer", win_report)
        self.assertIn("0.48", win_report)
        self.assertIn("compact:", win_report)
        self.assertIn("AdaptChart", win_report)
        self.assertIn("SizeType.Percent", win_report)
        self.assertIn("DockStyle.Fill", win_chart)
        self.assertIn("PixelOffsetMode.None", win_chart)
        self.assertIn("DrawFrame", win_chart)
        self.assertIn("Heading(\"按模型\")", win_report)
        self.assertIn("visibleFrame", mac_report)
        self.assertIn("maxHeight: 520", mac_report)
        self.assertIn("kpiStrip", mac_report)
        self.assertIn("PaintSelectedCell", win_chrome)
        self.assertIn("CellPainting", win_chrome)
        self.assertIn("class FlatCombo", win_chrome)
        self.assertIn("FitDropDownWidth", win_chrome)
        self.assertIn("class FlatDatePicker", win_chrome)
        self.assertIn("class FlatSpin", win_chrome)
        self.assertIn("class FlatCheck", win_chrome)
        self.assertIn("DarkMode_Explorer", win_chrome)
        self.assertIn("PreferAppDarkMode", win_chrome)
        self.assertIn("DarkScroll", win_chrome)
        self.assertIn("DarkScrollCover", win_chrome)
        self.assertIn("AllowDarkModeForWindow", win_chrome)
        self.assertIn("ComboButton", win_chrome)
        self.assertIn("DarkTree", win_chrome)
        self.assertIn("new FlatCombo", win_report)
        self.assertIn("new FlatDatePicker", win_report)
        self.assertIn("new FlatCombo", win_settings)
        self.assertIn("new FlatDatePicker", win_settings)
        self.assertIn("new FlatSpin", win_settings)
        self.assertIn("new FlatCheck", win_settings)
        self.assertIn("new Panel", win_compare)
        self.assertIn("WrapHint", win_compare)
        win_events = (root / "windows" / "CursorTokenCore" / "UsageEvents.cs").read_text(encoding="utf-8")
        mac_events = (root / "macos" / "Sources" / "CursorTokenCore" / "UsageEvents.swift").read_text(encoding="utf-8")
        self.assertIn("未填成本", win_events)
        self.assertIn("未填成本", mac_events)
        win_status = (root / "windows" / "CursorTokenCore" / "StatusText.cs").read_text(encoding="utf-8")
        mac_status = (root / "macos" / "Sources" / "CursorTokenCore" / "StatusText.swift").read_text(encoding="utf-8")
        for src in (win_status, mac_status):
            self.assertIn("按需不再按官网标价另加", src)
            self.assertIn("绿色数字", src)
            self.assertIn("已分摊", src)
            self.assertIn("本窗口折算", src)
            self.assertIn("未能拉取个人明细", src)
            self.assertIn("登录已过期，点下方", src)
            self.assertIn("已填成本", src)
        self.assertIn("FormatReportSyncError", win_status)
        self.assertIn("formatReportSyncError", mac_status)
        self.assertIn("FormatCloudDecryptNote", win_status)
        self.assertIn("formatCloudDecryptNote", mac_status)
        self.assertIn("FormatReportCacheStatus", win_status)
        self.assertIn("formatReportCacheStatus", mac_status)
        self.assertIn("FormatReportFilterEmpty", win_status)
        self.assertIn("formatReportFilterEmpty", mac_status)
        self.assertIn("FormatCompareSyncProgress", win_status)
        self.assertIn("formatCompareSyncProgress", mac_status)
        self.assertIn("FormatCloudSyncNotify", win_status)
        self.assertIn("formatCloudSyncNotify", mac_status)
        self.assertIn("FormatExportFilename", win_status)
        self.assertIn("formatExportFilename", mac_status)
        self.assertIn("FormatCompareAccountName", win_status)
        self.assertIn("formatCompareAccountName", mac_status)
        self.assertIn("FormatTokenSaveResult", win_status)
        self.assertIn("formatTokenSaveResult", mac_status)
        py_status = (root / "status_text.py").read_text(encoding="utf-8")
        self.assertIn("def format_report_filter_empty", py_status)
        self.assertIn("def format_compare_sync_progress", py_status)
        self.assertIn("def format_cloud_sync_notify", py_status)
        self.assertIn("def format_export_filename", py_status)
        self.assertIn("def format_compare_account_name", py_status)
        self.assertIn("def format_token_save_result", py_status)
        self.assertIn("def format_export_empty", py_status)
        self.assertIn("def format_account_menu_title", py_status)
        self.assertIn("FormatExportEmpty", win_status)
        self.assertIn("formatExportEmpty", mac_status)
        self.assertIn("FormatAccountMenuTitle", win_status)
        self.assertIn("formatAccountMenuTitle", mac_status)
        self.assertIn("Token 解不开", win_events)
        self.assertIn("Token 解不开", mac_events)
        self.assertIn("BuildAccountCompareReport", win_compare)
        self.assertIn("buildAccountCompareReport", mac_compare)
        self.assertIn("public void Reload()", win_compare)
        self.assertIn("_compare.Reload()", win_prog)
        self.assertIn("BoundedWork.Prioritize", win_compare)
        self.assertIn("RefreshGeneration.prioritize", mac_compare)
        self.assertIn("BoundedWork.MapAsync", win_compare)
        self.assertIn("RefreshGeneration.mapBounded", mac_compare)
        self.assertIn("FormatCompareSync", win_compare)
        self.assertIn("formatCompareSync", mac_compare)
        self.assertIn("ActiveAccountId", win_compare)
        self.assertIn("FormatReportSyncProgress", win_report)
        self.assertIn("formatReportSyncProgress", mac_report)
        self.assertIn("reloadForCurrentAccount", mac_report)
        self.assertIn("resetFilters", mac_report)
        self.assertIn("loadedAccountId", mac_report)
        self.assertIn("ResetFilters", win_report)
        self.assertIn("FormatReportFilterEmpty", win_report)
        self.assertIn("formatReportFilterEmpty", mac_report)
        self.assertIn("activeAccountId", mac_report)
        self.assertIn("FormatCompareSyncProgress", win_compare)
        self.assertIn("formatCompareSyncProgress", mac_compare)
        self.assertIn("TokenDecryptFailed", win_compare)
        self.assertIn("tokenDecryptFailed", mac_compare)
        self.assertIn("if store == nil", mac_compare)
        self.assertIn("reloadIfVisible", mac_compare)
        self.assertIn("reloadFromConfig", mac_compare)
        self.assertIn("FormatReportSyncError", win_report)
        self.assertIn("formatReportSyncError", mac_report)
        self.assertIn("FormatReportCacheStatus", win_report)
        self.assertIn("formatReportCacheStatus", mac_report)
        self.assertIn("_report.RequestSync()", win_prog)
        self.assertIn("FormatCloudDecryptNote", win_settings)
        self.assertIn("formatCloudDecryptNote", mac_settings)
        self.assertIn("if store == nil", mac_report)
        self.assertNotIn(".task { await store.sync() }", mac_report)
        self.assertIn("formatExportFilename", mac_report)
        self.assertIn("FormatExportFilename", win_report)
        self.assertIn("formatCompareAccountName", mac_compare)
        self.assertIn("FormatCompareAccountName", win_compare)
        self.assertIn("FormatTokenSaveResult", win_settings)
        self.assertIn("formatTokenSaveResult", mac_settings)
        self.assertIn("if window?.contentView == nil", mac_settings)
        self.assertIn("pendingCursorImport", mac_settings)
        self.assertIn("reloadFields", mac_settings)
        self.assertIn("settingsReloadTick", mac_settings)
        self.assertIn("formatExportEmpty", mac_report)
        self.assertIn("FormatExportEmpty", win_report)
        self.assertIn("formatExportEmpty", mac_compare)
        self.assertIn("FormatExportEmpty", win_compare)
        self.assertIn("reloadIfVisible", mac_report)
        win_flyout = (root / "windows" / "CursorRemain" / "FlyoutForm.cs").read_text(encoding="utf-8")
        mac_flyout = (root / "macos" / "Sources" / "CursorRemain" / "FlyoutView.swift").read_text(encoding="utf-8")
        mac_store = (root / "macos" / "Sources" / "CursorRemain" / "AppStore.swift").read_text(encoding="utf-8")
        self.assertIn("FlyoutSettingsTitle", win_flyout)
        self.assertIn("flyoutSettingsTitle", mac_flyout)
        self.assertIn("NSEvent.mouseLocation", mac_flyout)
        self.assertIn("CompareWindowController.shared.reloadIfVisible", mac_store)
        self.assertIn("ReportWindowController.shared.reloadIfVisible", mac_store)
        self.assertIn("settingsReloadTick", mac_store)
        self.assertIn("FormatCloudSyncNotify", win_prog)
        self.assertIn("formatCloudSyncNotify", mac_store)
        self.assertIn("pendingCursorImport", mac_store)
        switch_src = mac_store.split("func switchAccount")[1].split("func loginToCursor")[0]
        self.assertNotIn("FlyoutWindowController.shared.close()", switch_src)
        self.assertNotIn("flyoutVisible = false", switch_src)
        self.assertIn("IsAuthErrorMessage(_error)", win_prog)
        self.assertIn("Token.isAuthErrorMessage", mac_flyout)
        self.assertIn("TokenValues", (root / "windows" / "CursorRemain" / "UiForms.cs").read_text(encoding="utf-8"))
        self.assertIn("tokenValues", mac_settings)
        self.assertIn("allowedContentTypes", mac_settings)
        self.assertNotIn("allowedFileTypes", mac_settings)
        win_refresh = (root / "windows" / "CursorRemain" / "TrayRefresh.cs").read_text(encoding="utf-8")
        mac_store = (root / "macos" / "Sources" / "CursorRemain" / "AppStore.swift").read_text(encoding="utf-8")
        self.assertIn("BoundedWork.MapAsync", win_refresh)
        self.assertIn("RefreshGeneration.mapBounded", mac_store)
        self.assertNotIn("Task.WhenAll(remaining)", win_refresh)
        win_core_refresh = (root / "windows" / "CursorTokenCore" / "BoundedWork.cs").read_text(encoding="utf-8")
        mac_core_refresh = (root / "macos" / "Sources" / "CursorTokenCore" / "RefreshGeneration.swift").read_text(encoding="utf-8")
        self.assertIn("AccountRefreshLimit = 2", win_core_refresh)
        self.assertIn("accountRefreshLimit = 2", mac_core_refresh)
        self.assertIn("public static List<T> Prioritize", win_core_refresh)
        self.assertIn("public static func prioritize", mac_core_refresh)
        self.assertIn("TokenValues", (root / "windows" / "CursorTokenCore" / "CursorAccountPaste.cs").read_text(encoding="utf-8"))
        self.assertIn("func tokenValues", (root / "macos" / "Sources" / "CursorTokenCore" / "CursorAccountPaste.swift").read_text(encoding="utf-8"))
        self.assertIn("def token_values", (root / "cursor_login.py").read_text(encoding="utf-8"))
        self.assertIn("NormalizeReportRange", (root / "windows" / "CursorTokenCore" / "UsageEvents.cs").read_text(encoding="utf-8"))
        self.assertIn("normalizeReportRange", (root / "macos" / "Sources" / "CursorTokenCore" / "UsageEvents.swift").read_text(encoding="utf-8"))
        self.assertIn("TrimNote", (root / "windows" / "CursorTokenCore" / "AccountSync.cs").read_text(encoding="utf-8"))
        self.assertIn("func trimNote", (root / "macos" / "Sources" / "CursorTokenCore" / "AccountSync.swift").read_text(encoding="utf-8"))
        self.assertIn("全部额度", mac_report)
        self.assertIn("First-party", mac_report)
        self.assertIn("Grok Bot", mac_report)
        self.assertIn("开始日期", mac_report)
        self.assertIn("结束日期", mac_report)
        self.assertIn("persistReportRange", mac_report)
        self.assertIn("setReportRange", (root / "macos" / "Sources" / "CursorTokenCore" / "AppConfig.swift").read_text(encoding="utf-8"))
        self.assertIn("categoryFirstParty", mac_report)
        self.assertIn("企业额度（展示）", mac_report)
        self.assertIn("折扣", mac_report)
        self.assertIn("每百万Token", mac_report)
        self.assertIn("formatReportPerMillionKpi", mac_report)
        self.assertIn("formatReportSpendKpi", mac_report)
        self.assertNotIn("预计实付", mac_report)
        self.assertIn("ReportAllocationWindow", (root / "windows" / "CursorTokenCore" / "UsageEvents.cs").read_text(encoding="utf-8"))
        self.assertIn("struct ReportAllocationWindow", (root / "macos" / "Sources" / "CursorTokenCore" / "UsageEvents.swift").read_text(encoding="utf-8"))
        self.assertIn("FormatDiscount", (root / "windows" / "CursorTokenCore" / "UsageEvents.cs").read_text(encoding="utf-8"))
        self.assertIn("func formatDiscount", (root / "macos" / "Sources" / "CursorTokenCore" / "UsageEvents.swift").read_text(encoding="utf-8"))
        self.assertIn("def format_discount", (root / "usage_report.py").read_text(encoding="utf-8"))
        self.assertIn("NoteTeamPersonal", (root / "windows" / "CursorTokenCore" / "UsageEvents.cs").read_text(encoding="utf-8"))
        self.assertIn("noteTeamPersonal", (root / "macos" / "Sources" / "CursorTokenCore" / "UsageEvents.swift").read_text(encoding="utf-8"))
        self.assertIn("FormatFlyoutError", win_flyout)
        self.assertIn("formatFlyoutError", mac_flyout)
        self.assertIn("按小时", mac_chart)
        self.assertIn("buildChart", mac_report)
        self.assertNotIn("dailyChart", mac_report)

    def test_flyout_layout_matches_macos(self) -> None:
        from pathlib import Path

        root = Path(__file__).resolve().parents[1]
        win_layout = (root / "windows" / "CursorTokenCore" / "UiLayout.cs").read_text(encoding="utf-8")
        win_flyout = (root / "windows" / "CursorRemain" / "FlyoutForm.cs").read_text(encoding="utf-8")
        mac_flyout = (root / "macos" / "Sources" / "CursorRemain" / "FlyoutView.swift").read_text(encoding="utf-8")
        for snippet in (
            "static let width: CGFloat = 500",
            "static let height: CGFloat = 280",
            "static let cornerRadius: CGFloat = 16",
            "static let leftWidth: CGFloat = 176",
            "static let ringSize: CGFloat = 148",
            "static let toolButtonHeight: CGFloat = 28",
            "static let toolButtonGap: CGFloat = 6",
        ):
            self.assertIn(snippet, mac_flyout)
        for snippet in (
            "public const int Width = 500",
            "public const int Height = 280",
            "public const int CornerRadius = 16",
            "public const int LeftWidth = 176",
            "public const int RingSize = 148",
            "public const int ToolButtonHeight = 28",
            "public const int ToolButtonGap = 6",
        ):
            self.assertIn(snippet, win_layout)
        self.assertIn("DrawGauge", win_flyout)
        self.assertIn("DrawCard", win_flyout)
        self.assertIn("DashboardLinkLabel", win_flyout)
        self.assertIn("UiChrome.UiFont", win_flyout)
        self.assertIn("IconFont(11f)", win_flyout)
        self.assertIn("toolButton", mac_flyout)
        self.assertIn("var toolBar: some View", mac_flyout)
        self.assertIn(".fixedSize()", mac_flyout)
        self.assertIn("DrawButtons(g, new RectangleF(pad, contentBottom + btnGap, Width - pad * 2, btnH)", win_flyout)
        self.assertIn("FlyoutLayout.ToolButtonSize", win_flyout)
        self.assertIn("FlyoutLayout.ArrangeToolButtons", win_flyout)
        self.assertIn("containerWidth - packed", win_layout)
        self.assertIn("Spacer(minLength: 0)", mac_flyout)
        self.assertIn("DrawFittedString", win_flyout)
        self.assertIn("StringTrimming.None", win_flyout)
        self.assertIn("public static (float Width, float Height) ToolButtonSize", win_layout)
        self.assertIn("ArrangeToolButtons", win_layout)
        self.assertIn("InnerStroke", win_layout)
        self.assertIn("RoundRegionDiameter", win_layout)
        self.assertIn("CreateRoundRectRgn", win_flyout)
        self.assertIn("DwmwcpDoNotRound", win_flyout)
        self.assertIn("FlyoutLayout.InnerStroke", win_flyout)
        self.assertIn("Grok Bot", win_flyout)
        self.assertIn("Grok Bot", mac_flyout)
        self.assertIn("_dailyAvg = dailyAvg", win_flyout)
        self.assertIn("TrendSummary", win_flyout)
        self.assertIn("trendText", mac_flyout)
        self.assertIn("infoMeta", mac_flyout)
        self.assertNotIn("DrawTrendLine", win_flyout)
        self.assertNotIn("var trendLine: some View", mac_flyout)
        self.assertNotIn("DrawSparkline", win_flyout)
        self.assertNotIn("struct Sparkline", mac_flyout)
        self.assertNotIn("_body.Text", win_flyout)
        win_spark = (root / "windows" / "CursorTokenCore" / "UiLayout.cs").read_text(encoding="utf-8")
        mac_spark = (root / "macos" / "Sources" / "CursorTokenCore" / "SparklineGeometry.swift").read_text(encoding="utf-8")
        for src in (win_spark, mac_spark):
            self.assertIn("刷新几次后显示近日消耗", src)
            self.assertIn("用掉", src)
            self.assertIn("几乎没消耗", src)
            self.assertIn("近 1 日", src)
        inner = 500 - 16 * 2
        right = inner - 176 - 16
        need = 5 * 56 + 4 * 6
        self.assertGreaterEqual(inner, need)
        self.assertLess(right, need)

    def test_grok_bot_status_lines(self) -> None:
        from datetime import datetime, timezone

        from status_text import build_status_lines, format_summary_text

        snap = parse_usage_summary(PERSONAL_ULTRA)
        apply_sand_usage_status(
            snap,
            {
                "currentPeriodStart": "2026-08-17T07:57:50.647Z",
                "nextResetTimestampUtc": "2026-08-24T07:57:50.647Z",
                "usagePercent": 58.3,
                "hasNonZeroIncludedLimit": True,
            },
            now=datetime(2026, 8, 20, 7, 57, 50, 647000, tzinfo=timezone.utc),
        )
        self.assertTrue(snap.shows_grok_bot())
        self.assertEqual(snap.grok_bot_percent_used, 58.3)
        summary = format_summary_text(snap, None, "12:00")
        self.assertIn("Grok Bot 剩余 41.7%", summary)
        lines = dict(build_status_lines(snap, None, "12:00"))
        self.assertIn("Grok Bot", lines)
        self.assertIn("本周已用 58.3%", lines["Grok Bot"])
        reset = datetime.fromisoformat("2026-08-24T07:57:50.647+00:00").astimezone()
        self.assertIn(f"{reset.month}月{reset.day}日", lines["Grok Bot"])

    def test_compare_sync_and_trim_status_copy(self) -> None:
        from cursor_login import token_values
        from status_text import (
            compare_hint,
            flyout_settings_title,
            format_cloud_decrypt_note,
            format_compare_hint,
            format_compare_sync,
            format_flyout_error,
            format_cloud_sync_notify,
            format_compare_account_name,
            format_compare_sync_progress,
            format_export_filename,
            format_account_menu_title,
            format_export_empty,
            format_token_save_result,
            format_report_cache_status,
            format_report_filter_empty,
            format_report_per_million_kpi,
            format_report_spend_kpi,
            format_report_sync_error,
            format_report_sync_progress,
            format_report_sync_result,
            format_sync_status,
            prioritize_active,
        )

        self.assertEqual(format_report_sync_progress(1), "正在同步本周期明细…")
        self.assertEqual(format_report_sync_progress(3), "正在同步本周期明细…第 3 页")
        self.assertEqual(format_report_filter_empty(0), "")
        self.assertEqual(format_report_filter_empty(12), "当前筛选无结果（本地共 12 条，可清空筛选）")
        self.assertEqual(format_compare_sync_progress(2, 5, "工作号"), "正在同步 工作号（2/5）…")
        self.assertEqual(format_compare_sync_progress(2, 5, "工作号", 3), "正在同步 工作号（2/5）…第 3 页")
        self.assertEqual(format_cloud_sync_notify(True, "登录已过期，请重新登录"), "")
        self.assertEqual(format_cloud_sync_notify(False, "登录已过期，请重新登录"), "登录已过期，请重新登录")
        self.assertEqual(format_cloud_sync_notify(False, "用量明细因体积限制裁掉了 3 条最旧记录"), "")
        self.assertEqual(format_export_filename("cursor-usage", "工作号", "20260919"), "cursor-usage-工作号-20260919.csv")
        self.assertEqual(format_export_filename("cursor-account-compare", "", "20260919"), "cursor-account-compare-20260919.csv")
        self.assertEqual(format_compare_account_name("工作号", True), "工作号  · 当前")
        self.assertEqual(format_compare_account_name("工作号", False), "工作号")
        self.assertEqual(format_token_save_result(1, 0), "")
        self.assertEqual(format_token_save_result(2, 0), "已保存 2 个账号")
        self.assertEqual(format_token_save_result(1, 1), "成功 1 / 失败 1")
        self.assertEqual(format_export_empty(), "当前没有可导出的明细")
        self.assertEqual(format_account_menu_title("工作号", 42.4), "工作号  42%")
        self.assertEqual(format_account_menu_title("", None), "未命名账号")
        self.assertEqual(format_report_per_million_kpi(10, 2_000_000), "≈¥5.00")
        self.assertEqual(format_report_per_million_kpi(10, 0), "")
        kpi = format_report_spend_kpi(75, 150, 37.5, 7.5, True, 75)
        self.assertIn("已分摊", kpi)
        self.assertIn("本窗口折算", kpi)
        self.assertNotIn("预计实付", kpi)
        self.assertEqual(
            format_report_sync_result(3, 0, "12:00:00", note="team_personal"),
            "未能拉取个人明细（团队账号）。请先刷新用量，或把范围切到「全员」。",
        )
        self.assertIn("已是最新", format_report_sync_result(12, 0, "12:00:00"))
        self.assertIn("新增 4", format_report_sync_result(16, 4, "12:00:00"))
        self.assertEqual(format_flyout_error("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"), "登录已过期，点下方「粘贴 Token」更新")
        self.assertEqual(format_flyout_error("未配置 Token，请打开设置粘贴"), "未配置 Token，点下方「粘贴 Token」导入")
        self.assertEqual(format_flyout_error("HTTP 429"), "HTTP 429")
        self.assertIn("绿色数字", compare_hint())
        self.assertIn("已填成本", compare_hint())
        self.assertIn("绿色仅供参考", format_compare_hint(True))
        self.assertEqual(format_compare_hint(False), compare_hint())
        self.assertEqual(format_report_cache_status(12, "工作号"), "当前：工作号 · 本地 12 条，正在刷新…")
        self.assertEqual(format_report_cache_status(0), "本地还没有明细，正在同步…")
        self.assertEqual(
            format_report_sync_error("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"),
            "登录已过期，请到设置重新粘贴 Token",
        )
        self.assertEqual(format_report_sync_error("未配置 Token，请打开设置粘贴"), "未配置 Token，请先在设置里导入账号")
        self.assertTrue(format_report_sync_error("HTTP 429").startswith("同步失败："))
        self.assertIn("解不开云同步密钥", format_cloud_decrypt_note(False, True, False))
        self.assertIn("重新粘贴 Token", format_cloud_decrypt_note(True, False, False))
        self.assertEqual(format_cloud_decrypt_note(False, False, False), "")
        self.assertEqual(flyout_settings_title(None), "设置")
        self.assertEqual(flyout_settings_title("HTTP 429"), "设置")
        self.assertEqual(flyout_settings_title("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"), "粘贴 Token")
        self.assertEqual(flyout_settings_title("未配置 Token，请打开设置粘贴"), "粘贴 Token")
        self.assertEqual(prioritize_active(["b", "a", "c"], "a"), ["a", "b", "c"])
        self.assertEqual(token_values("aaa.bbb.ccc\nddd.eee.fff"), ["aaa.bbb.ccc", "ddd.eee.fff"])
        self.assertEqual(token_values("name@example.com:secret"), [])
        self.assertEqual(format_compare_sync(3, [], "12:00:00"), "已同步 3 个账号  ·  12:00:00")
        self.assertIn(
            "工作号：Token 过期",
            format_compare_sync(2, ["工作号：Token 过期", "临时号：未配置 Token"], "12:01:00"),
        )
        long = format_compare_sync(1, ["a：1", "b：2", "c：3", "d：4"], "12:02:00")
        self.assertIn("等4个", long)
        self.assertNotIn("d：4", long)
        self.assertEqual(format_sync_status("", ""), "")
        self.assertIn("上次同步", format_sync_status("2026-09-19T04:00:00.000Z", ""))
        self.assertEqual(format_sync_status("", "登录已过期，请重新登录"), "登录已过期，请重新登录")
        mixed = format_sync_status("2026-09-19T04:00:00.000Z", "用量明细因体积限制裁掉了 12 条最旧记录")
        self.assertIn("上次同步", mixed)
        self.assertIn("体积限制", mixed)

    def test_compare_best_skips_zero_cost_rows(self) -> None:
        from usage_report import (
            AccountCompareCategory,
            AccountCompareRow,
            compare_best_eligible,
            compare_best_per_million,
            compare_row_note,
            compare_windows_mixed,
        )

        empty = AccountCompareCategory(category="first_party")
        unpaid = AccountCompareRow(
            account_id="a",
            label="",
            channel="",
            membership_type="",
            window_source="fallback",
            window_start_ms=0,
            window_end_ms=0,
            window_days=30,
            plan_cny=0,
            daily_holding_cny=0,
            window_plan_cny=0,
            on_demand_cny=0,
            total_cny=0,
            event_count=2,
            total_tokens=2_000_000,
            first_party=empty,
            api=empty,
            grok_bot=empty,
        )
        paid = AccountCompareRow(
            account_id="b",
            label="",
            channel="",
            membership_type="",
            window_source="cycle",
            window_start_ms=0,
            window_end_ms=0,
            window_days=15,
            plan_cny=150,
            daily_holding_cny=5,
            window_plan_cny=75,
            on_demand_cny=0,
            total_cny=75,
            event_count=1,
            total_tokens=1_000_000,
            first_party=empty,
            api=empty,
            grok_bot=empty,
        )
        other = AccountCompareRow(
            account_id="c",
            label="",
            channel="",
            membership_type="",
            window_source="validity",
            window_start_ms=0,
            window_end_ms=0,
            window_days=20,
            plan_cny=150,
            daily_holding_cny=5,
            window_plan_cny=150,
            on_demand_cny=0,
            total_cny=30,
            event_count=1,
            total_tokens=1_000_000,
            first_party=empty,
            api=empty,
            grok_bot=empty,
        )
        self.assertFalse(compare_best_eligible(unpaid))
        self.assertEqual(compare_row_note(unpaid), "未填成本")
        self.assertEqual(compare_row_note(paid, has_token=False), "未配置 Token")
        self.assertEqual(compare_row_note(paid, last_error="Token 已过期或无效"), "登录已过期")
        self.assertEqual(compare_row_note(paid, has_token=False, last_error="Token 解密失败，请重新导入"), "Token 解不开")
        self.assertEqual(compare_best_per_million([unpaid, paid, other]), 30)
        self.assertTrue(compare_windows_mixed([paid, other]))
        self.assertFalse(compare_windows_mixed([paid, unpaid]))
