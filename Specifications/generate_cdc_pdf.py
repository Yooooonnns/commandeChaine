from __future__ import annotations

from pathlib import Path
import re
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import cm
from reportlab.pdfgen.canvas import Canvas
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak, Image, Table, TableStyle
from reportlab.graphics.shapes import Drawing, Rect, String, Line, Ellipse


def _escape(text: str) -> str:
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    )


def _build_diagram_image(
    image_path: Path,
    max_width: float = 16.5 * cm,
    max_height: float = 9.8 * cm,
    zoom_factor: float = 1.08,
):
    image = Image(str(image_path))
    width, height = image.imageWidth, image.imageHeight
    fit_scale = min(max_width / width, max_height / height)
    scale = min(fit_scale, zoom_factor)
    image.drawWidth = width * scale
    image.drawHeight = height * scale
    image.hAlign = "CENTER"
    return image


def parse_markdown_to_flowables(markdown_text: str, styles: dict, diagram_images: dict[str, Path]) -> list:
    lines = markdown_text.splitlines()
    flowables = []

    in_code_block = False
    in_table = False
    table_data = []
    
    i = 0
    while i < len(lines):
        raw_line = lines[i]
        line = raw_line.rstrip("\n")
        stripped = line.strip()

        # Check if we're starting or ending a table
        if stripped.startswith("|") and not in_code_block:
            if not in_table:
                in_table = True
                table_data = []
            
            # Parse table row
            cells = [cell.strip() for cell in stripped.split("|")[1:-1]]
            
            # Check if this is a separator row (all cells are dashes and colons)
            is_separator = all(re.match(r'^:?-+:?$', cell) for cell in cells)
            if not is_separator:
                table_data.append(cells)
            
            i += 1
            
            # Check if next line is still part of table
            if i < len(lines):
                next_line = lines[i].strip()
                if not next_line.startswith("|"):
                    # End of table, render it
                    if len(table_data) > 0:
                        flowables.append(_build_table(table_data, styles))
                        flowables.append(Spacer(1, 0.3 * cm))
                    in_table = False
                    table_data = []
            continue

        # Handle end of table at end of document
        if in_table and not stripped.startswith("|"):
            if len(table_data) > 0:
                flowables.append(_build_table(table_data, styles))
                flowables.append(Spacer(1, 0.3 * cm))
            in_table = False
            table_data = []

        if stripped.startswith("```"):
            in_code_block = not in_code_block
            i += 1
            continue

        if in_code_block:
            flowables.append(Paragraph(_escape(line) or " ", styles["Code"]))
            i += 1
            continue

        if not stripped:
            flowables.append(Spacer(1, 0.18 * cm))
            i += 1
            continue

        if stripped == "---":
            flowables.append(Spacer(1, 0.2 * cm))
            i += 1
            continue

        heading_match = re.match(r"^(#{1,4})\s+(.+)$", stripped)
        if heading_match:
            level = len(heading_match.group(1))
            raw_title = heading_match.group(2).strip()
            title = _escape(raw_title)
            if level == 1:
                flowables.append(Paragraph(title, styles["H1"]))
                flowables.append(Spacer(1, 0.25 * cm))
            elif level == 2:
                flowables.append(Paragraph(title, styles["H2"]))
                flowables.append(Spacer(1, 0.15 * cm))
            elif level == 3:
                flowables.append(Paragraph(title, styles["H3"]))
            else:
                flowables.append(Paragraph(title, styles["H4"]))

            # Keep diagrams inside their functional sections
            if raw_title.startswith("4.1 System Context Diagram") or raw_title.startswith("4.2 Logical IT Architecture"):
                flowables.append(Spacer(1, 0.2 * cm))
                image_path = diagram_images.get("context")
                if image_path and image_path.exists():
                    flowables.append(_build_diagram_image(image_path))
                else:
                    flowables.append(build_system_context_diagram())
                flowables.append(Spacer(1, 0.3 * cm))

            if raw_title.startswith("5.1 Use-Case Diagram Set"):
                flowables.append(Spacer(1, 0.2 * cm))
                image_path = diagram_images.get("usecase")
                if image_path and image_path.exists():
                    flowables.append(_build_diagram_image(image_path))
                else:
                    flowables.append(build_use_case_diagram())
                flowables.append(Spacer(1, 0.3 * cm))

            if raw_title.startswith("6. End-to-End Workflow"):
                flowables.append(Spacer(1, 0.2 * cm))
                image_path = diagram_images.get("workflow")
                if image_path and image_path.exists():
                    flowables.append(_build_diagram_image(image_path))
                else:
                    flowables.append(build_workflow_diagram())
                flowables.append(Spacer(1, 0.3 * cm))
            
            i += 1
            continue

        if stripped.startswith("- "):
            bullet_text = _escape(stripped[2:].strip())
            flowables.append(Paragraph(f"• {bullet_text}", styles["Bullet"]))
            i += 1
            continue

        if re.match(r"^\d+\.\s+", stripped):
            flowables.append(Paragraph(_escape(stripped), styles["Body"]))
            i += 1
            continue

        inline_code_converted = re.sub(r"`([^`]+)`", r"<font name='Courier'>\1</font>", _escape(stripped))
        flowables.append(Paragraph(inline_code_converted, styles["Body"]))
        i += 1

    return flowables


def _build_table(table_data: list, styles: dict) -> Table:
    """Build a reportlab Table from parsed markdown table data."""
    if not table_data:
        return Spacer(1, 0)
    
    # Convert table cells to Paragraphs for better formatting
    processed_data = []
    for row in table_data:
        processed_row = []
        for cell in row:
            # Handle bold formatting
            cell_text = _escape(cell)
            cell_text = re.sub(r'\*\*([^*]+)\*\*', r'<b>\1</b>', cell_text)
            # Handle inline code
            cell_text = re.sub(r"`([^`]+)`", r"<font name='Courier'>\1</font>", cell_text)
            # Handle special symbols (checkmarks, etc.)
            cell_text = cell_text.replace('✓', '✓').replace('⚙', '⚙').replace('⚠', '⚠')
            processed_row.append(Paragraph(cell_text, styles["Body"]))
        processed_data.append(processed_row)
    
    # Create table
    table = Table(processed_data)
    
    # Apply table style
    table.setStyle(TableStyle([
        # Header row styling
        ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor("#2A4A6E")),
        ('TEXTCOLOR', (0, 0), (-1, 0), colors.whitesmoke),
        ('ALIGN', (0, 0), (-1, -1), 'LEFT'),
        ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
        ('FONTSIZE', (0, 0), (-1, 0), 9),
        ('BOTTOMPADDING', (0, 0), (-1, 0), 8),
        ('TOPPADDING', (0, 0), (-1, 0), 8),
        
        # Body rows styling
        ('BACKGROUND', (0, 1), (-1, -1), colors.HexColor("#F9FBFD")),
        ('ROWBACKGROUNDS', (0, 1), (-1, -1), [colors.HexColor("#F9FBFD"), colors.HexColor("#FFFFFF")]),
        ('FONTNAME', (0, 1), (-1, -1), 'Helvetica'),
        ('FONTSIZE', (0, 1), (-1, -1), 8),
        ('TOPPADDING', (0, 1), (-1, -1), 6),
        ('BOTTOMPADDING', (0, 1), (-1, -1), 6),
        ('LEFTPADDING', (0, 0), (-1, -1), 6),
        ('RIGHTPADDING', (0, 0), (-1, -1), 6),
        
        # Grid styling
        ('GRID', (0, 0), (-1, -1), 0.5, colors.HexColor("#C5D4E3")),
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
    ]))
    
    return table


def _center_label(d: Drawing, x: float, y: float, w: float, h: float, label: str, font_size: float = 9.0) -> None:
    lines = label.split("\n")
    line_height = font_size + 1.5
    total_height = line_height * len(lines)
    start_y = y + (h / 2) + (total_height / 2) - line_height
    for i, line in enumerate(lines):
        d.add(
            String(
                x + w / 2,
                start_y - i * line_height,
                line,
                textAnchor="middle",
                fontName="Helvetica-Bold",
                fontSize=font_size,
                fillColor=colors.HexColor("#12304F"),
            )
        )


def _box(d: Drawing, x: float, y: float, w: float, h: float, label: str, fill: str = "#EFF4FA") -> None:
    d.add(Rect(x, y, w, h, rx=8, ry=8, fillColor=colors.HexColor(fill), strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.1))
    _center_label(d, x, y, w, h, label, font_size=9)


def _usecase(d: Drawing, x: float, y: float, w: float, h: float, label: str) -> None:
    d.add(
        Rect(
            x,
            y,
            w,
            h,
            rx=18,
            ry=18,
            strokeColor=colors.HexColor("#2A4A6E"),
            fillColor=colors.HexColor("#ECF4FC"),
            strokeWidth=1,
        )
    )
    _center_label(d, x, y, w, h, label, font_size=8.5)


def _arrow(d: Drawing, x1: float, y1: float, x2: float, y2: float) -> None:
    d.add(Line(x1, y1, x2, y2, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    dx = x2 - x1
    dy = y2 - y1
    length = (dx * dx + dy * dy) ** 0.5 or 1.0
    ux, uy = dx / length, dy / length
    px, py = -uy, ux
    size = 5
    d.add(Line(x2, y2, x2 - ux * size + px * 2.2, y2 - uy * size + py * 2.2, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    d.add(Line(x2, y2, x2 - ux * size - px * 2.2, y2 - uy * size - py * 2.2, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))


def _edge_label(d: Drawing, x: float, y: float, text: str) -> None:
    width = max(34, len(text) * 3.9)
    d.add(Rect(x - width / 2, y - 7, width, 13, rx=4, ry=4, fillColor=colors.HexColor("#F9FCFF"), strokeColor=colors.HexColor("#BBD1E6"), strokeWidth=0.5))
    d.add(String(x, y - 1.5, text, textAnchor="middle", fontName="Helvetica", fontSize=7, fillColor=colors.HexColor("#24496E")))


def _actor(d: Drawing, x: float, y: float, name: str, side: str) -> None:
    # simple stick figure actor
    d.add(Line(x, y + 8, x, y + 32, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1))
    d.add(Line(x - 8, y + 22, x + 8, y + 22, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1))
    d.add(Line(x, y + 8, x - 7, y - 2, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1))
    d.add(Line(x, y + 8, x + 7, y - 2, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1))
    d.add(Ellipse(x - 5, y + 34, 10, 10, strokeColor=colors.HexColor("#2A4A6E"), fillColor=colors.HexColor("#ECF4FC"), strokeWidth=1))
    lines = name.split("\n")
    label_y = y - 12
    for i, line in enumerate(lines):
        d.add(String(x, label_y - (i * 9), line, textAnchor="middle", fontName="Helvetica-Bold", fontSize=8.2, fillColor=colors.HexColor("#12304F")))


def build_system_context_diagram() -> Drawing:
    d = Drawing(500, 280)
    d.add(String(250, 260, "System Context Diagram", textAnchor="middle", fontName="Helvetica-Bold", fontSize=12.5, fillColor=colors.HexColor("#0F2D52")))

    # Top row (IT flow)
    _box(d, 20, 178, 100, 52, "Production\nSystems", "#F2F7FC")
    _box(d, 145, 178, 105, 52, "IT/API\n(.NET)", "#E6F0FA")
    _box(d, 275, 178, 100, 52, "MQTT\nBroker", "#EAF3FB")
    _box(d, 395, 178, 100, 52, "Raspberry Pi\n(Python)", "#E6F0FA")

    # Bottom row (visualization and actuation)
    _box(d, 145, 78, 105, 52, "Desktop UX\n(WPF)", "#F2F7FC")
    _box(d, 395, 78, 100, 52, "DAC / VFD\nControl", "#F2F7FC")
    _box(d, 20, 20, 100, 52, "Rotary Encoder\nFeedback", "#F9F2EA")

    # Main IT command flow
    _arrow(d, 120, 204, 145, 204)
    _arrow(d, 250, 204, 275, 204)
    _arrow(d, 375, 204, 395, 204)

    # Monitoring and actuation branches
    _arrow(d, 197, 178, 197, 130)
    _arrow(d, 445, 178, 445, 130)

    # Chain-state feedback path (encoder -> Raspberry decision logic), orthogonal routing
    d.add(Line(120, 46, 120, 30, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    d.add(Line(120, 30, 445, 30, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    d.add(Line(445, 30, 445, 170, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    _arrow(d, 445, 170, 445, 178)

    # Labels
    _edge_label(d, 133, 217, "FO / Board Events")
    _edge_label(d, 262, 217, "CT Payload")
    _edge_label(d, 385, 217, "Subscribe + Validate")
    _edge_label(d, 197, 141, "REST + SignalR")
    _edge_label(d, 445, 141, "Speed -> Voltage")
    _edge_label(d, 282, 42, "Chain-State Feedback")
    return d


def build_use_case_diagram() -> Drawing:
    d = Drawing(480, 405)
    center_x = 240
    d.add(String(center_x, 384, "Use-Case Overview", textAnchor="middle", fontName="Helvetica-Bold", fontSize=12.5, fillColor=colors.HexColor("#0F2D52")))

    d.add(Rect(80, 18, 320, 344, rx=10, ry=10, strokeColor=colors.HexColor("#2A4A6E"), fillColor=colors.HexColor("#FBFDFF"), strokeWidth=1.2))
    d.add(String(center_x, 340, "Yazaki Line Speed Control", textAnchor="middle", fontName="Helvetica-Bold", fontSize=9.5, fillColor=colors.HexColor("#12304F")))

    left_x = 96
    right_x = 238
    uc_w = 124
    usecases = [
        (left_x, 276, "Collect Production Data"),
        (right_x, 276, "Calculate CT"),
        (left_x, 236, "Publish CT via MQTT"),
        (right_x, 236, "Receive and Validate CT"),
        (left_x, 196, "Convert CT to Speed"),
        (right_x, 196, "Control Conveyor Speed"),
        (left_x, 156, "Monitor Performance"),
        (right_x, 156, "Configure Parameters"),
        (194, 110, "Manage Abnormal Situations"),
    ]
    for x, y, label in usecases:
        _usecase(d, x, y, uc_w, 24, label)

    _actor(d, 42, 270, "IT Production\nSystem", side="left")
    _actor(d, 438, 270, "Raspberry Pi\nController", side="right")
    _actor(d, 42, 138, "Production\nManager", side="left")
    _actor(d, 438, 138, "Maintenance\nEngineer", side="right")

    _arrow(d, 52, 292, left_x, 288)
    _arrow(d, 52, 152, left_x, 168)
    _arrow(d, 428, 292, right_x + uc_w, 248)
    _arrow(d, 428, 152, right_x + uc_w, 168)
    _arrow(d, 428, 152, 290, 122)

    # Internal relationships with reserved label space
    _arrow(d, left_x + uc_w, 288, right_x, 288)
    _edge_label(d, center_x, 302, "input")

    _arrow(d, right_x + 56, 276, right_x + 56, 248)
    _edge_label(d, 332, 262, "result")

    _arrow(d, left_x + uc_w, 248, right_x, 248)
    _edge_label(d, center_x, 262, "message")

    _arrow(d, right_x, 236, left_x + uc_w, 208)
    _edge_label(d, 222, 232, "validated")

    _arrow(d, left_x + uc_w, 208, right_x, 208)
    _edge_label(d, center_x + 4, 216, "command")

    _arrow(d, right_x, 196, left_x + uc_w, 168)
    _edge_label(d, 232, 186, "telemetry")

    # rules: Configure Parameters -> Receive and Validate CT (routed on right corridor)
    d.add(Line(right_x + 56, 168, 388, 168, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    d.add(Line(388, 168, 388, 248, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    _arrow(d, 388, 248, right_x + uc_w, 248)
    _edge_label(d, 400, 208, "rules")

    _arrow(d, left_x + uc_w, 168, 250, 134)
    _edge_label(d, 226, 154, "alert")

    _arrow(d, 306, 122, right_x + 56, 156)
    _edge_label(d, 334, 140, "adjust")
    return d


def build_workflow_diagram() -> Drawing:
    d = Drawing(500, 300)
    d.add(String(250, 282, "Runtime Sequence Workflow", textAnchor="middle", fontName="Helvetica-Bold", fontSize=12.5, fillColor=colors.HexColor("#0F2D52")))

    lanes = [
        (50, "Production"),
        (150, "IT/API"),
        (250, "MQTT"),
        (350, "Raspberry"),
        (450, "Encoder"),
    ]

    for x, label in lanes:
        _box(d, x - 40, 228, 80, 30, label, "#EFF5FC")
        d.add(Line(x, 226, x, 90, strokeColor=colors.HexColor("#AFC6DD"), strokeWidth=1, strokeDashArray=[3, 2]))

    # Main message sequence
    _arrow(d, 50, 206, 150, 206)
    _edge_label(d, 100, 218, "Production + JIG Data")

    _arrow(d, 150, 178, 250, 178)
    _edge_label(d, 200, 190, "CT = MH / NOP")

    _arrow(d, 250, 150, 350, 150)
    _edge_label(d, 300, 162, "Publish CT + JIGs")

    _arrow(d, 450, 122, 350, 122)
    _edge_label(d, 400, 134, "Position Feedback")

    _arrow(d, 350, 122, 350, 104)
    _edge_label(d, 405, 112, "Filter + Clamp + Ramp")

    _arrow(d, 350, 104, 350, 88)
    _edge_label(d, 402, 94, "Apply to DAC/VFD")

    # Bottom actions (separated from lifelines)
    _box(d, 60, 26, 120, 32, "Fallback Decision", "#FFF6E8")
    _box(d, 190, 26, 130, 32, "State Feedback Merge", "#EAF1FF")
    _box(d, 330, 26, 130, 32, "Log and Export", "#EEF9EE")

    _arrow(d, 180, 42, 190, 42)
    _arrow(d, 320, 42, 330, 42)

    # Raspberry -> State Feedback Merge orthogonal path
    d.add(Line(350, 100, 350, 70, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    d.add(Line(350, 70, 255, 70, strokeColor=colors.HexColor("#2A4A6E"), strokeWidth=1.0))
    _arrow(d, 255, 70, 255, 58)
    _edge_label(d, 305, 78, "Running/Stopped feedback")
    return d


def draw_cover(canvas: Canvas, doc) -> None:
    width, height = A4
    canvas.saveState()

    canvas.setFillColor(colors.HexColor("#0E2A47"))
    canvas.rect(0, 0, width, height, fill=1, stroke=0)

    canvas.setFillColor(colors.HexColor("#123A61"))
    canvas.roundRect(-30, height - 220, width + 60, 180, 30, fill=1, stroke=0)

    canvas.setFillColor(colors.HexColor("#1C4E7C"))
    canvas.roundRect(40, 120, width - 80, 220, 18, fill=1, stroke=0)

    canvas.setFillColor(colors.white)
    canvas.setFont("Helvetica-Bold", 28)
    canvas.drawString(62, height - 140, "YAZAKI")
    canvas.setFont("Helvetica", 16)
    canvas.drawString(62, height - 168, "Line Speed Control")

    canvas.setFont("Helvetica-Bold", 21)
    canvas.drawString(62, 290, "Product Specification")
    canvas.setFont("Helvetica", 12)
    canvas.drawString(62, 264, "Workflow, UX, Data Integration, and Control Architecture")

    canvas.setFillColor(colors.HexColor("#D3E6F7"))
    canvas.setFont("Helvetica", 10.5)
    canvas.drawString(62, 230, "Date: 2026-02-19")
    canvas.drawString(62, 214, "Prepared for management review")

    canvas.setFillColor(colors.HexColor("#7FB1DA"))
    canvas.circle(width - 95, 110, 52, stroke=0, fill=1)
    canvas.setFillColor(colors.HexColor("#A9CEE8"))
    canvas.circle(width - 95, 110, 35, stroke=0, fill=1)
    canvas.setFillColor(colors.HexColor("#DDECF8"))
    canvas.circle(width - 95, 110, 18, stroke=0, fill=1)

    canvas.restoreState()


def draw_footer(canvas: Canvas, doc) -> None:
    canvas.saveState()
    canvas.setFillColor(colors.HexColor("#5C6F82"))
    canvas.setFont("Helvetica", 8)
    canvas.drawRightString(A4[0] - 1.8 * cm, 0.9 * cm, f"Page {doc.page}")
    canvas.drawString(1.8 * cm, 0.9 * cm, "Yazaki Line Speed Control")
    canvas.restoreState()


def build_pdf(input_md: Path, output_pdf: Path) -> None:
    styles_base = getSampleStyleSheet()
    styles = {
        "H1": ParagraphStyle(
            "H1",
            parent=styles_base["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=19,
            leading=24,
            textColor=colors.HexColor("#0F2D52"),
            spaceAfter=7,
        ),
        "H2": ParagraphStyle(
            "H2",
            parent=styles_base["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=14,
            leading=18,
            textColor=colors.HexColor("#173D70"),
            spaceBefore=8,
            spaceAfter=5,
        ),
        "H3": ParagraphStyle(
            "H3",
            parent=styles_base["Heading3"],
            fontName="Helvetica-Bold",
            fontSize=12,
            leading=15,
            textColor=colors.HexColor("#1F4A88"),
            spaceBefore=5,
            spaceAfter=3,
        ),
        "H4": ParagraphStyle(
            "H4",
            parent=styles_base["Heading4"],
            fontName="Helvetica-Bold",
            fontSize=11,
            leading=14,
            textColor=colors.HexColor("#2A5AA2"),
            spaceBefore=4,
            spaceAfter=2,
        ),
        "Body": ParagraphStyle(
            "Body",
            parent=styles_base["BodyText"],
            fontName="Helvetica",
            fontSize=10.5,
            leading=14,
            spaceAfter=4,
            textColor=colors.HexColor("#222222"),
        ),
        "Bullet": ParagraphStyle(
            "Bullet",
            parent=styles_base["BodyText"],
            fontName="Helvetica",
            fontSize=10.5,
            leading=14,
            leftIndent=12,
            spaceAfter=3,
            textColor=colors.HexColor("#222222"),
        ),
        "Code": ParagraphStyle(
            "Code",
            parent=styles_base["Code"],
            fontName="Courier",
            fontSize=8.8,
            leading=11,
            backColor=colors.HexColor("#F3F5F7"),
            leftIndent=6,
            rightIndent=6,
            spaceAfter=2,
        ),
    }

    md_text = input_md.read_text(encoding="utf-8")
    diagram_images = {
        "usecase": input_md.parent / "usecase.png",
        "context": input_md.parent / "mermaid-diagram-2026-02-20-114434.png",
        "workflow": input_md.parent / "mermaid-diagram-2026-02-20-114531.png",
    }

    doc = SimpleDocTemplate(
        str(output_pdf),
        pagesize=A4,
        rightMargin=1.8 * cm,
        leftMargin=1.8 * cm,
        topMargin=1.6 * cm,
        bottomMargin=1.6 * cm,
        title="Yazaki Line Speed Control",
        author="Yazaki Commande Chaine Team",
        subject="Product specification aligned with TPME structure",
    )

    story = []
    story.append(Spacer(1, 24 * cm))
    story.append(PageBreak())

    story.extend(parse_markdown_to_flowables(md_text, styles, diagram_images))

    doc.build(story, onFirstPage=draw_cover, onLaterPages=draw_footer)


def main() -> None:
    base = Path(__file__).resolve().parent
    input_md = base / "CDC_Yazaki_CommandeChaine_POC.md"
    output_pdf = base / "Yazaki_Line_Speed_Control_EN.pdf"

    if not input_md.exists():
        raise FileNotFoundError(f"Source markdown introuvable: {input_md}")

    build_pdf(input_md, output_pdf)
    print(f"PDF genere: {output_pdf}")


if __name__ == "__main__":
    main()
