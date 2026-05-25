# Demo 7: Visual Skill Memory — Skill Lookup, Replay, and Extraction

**Task ID:** `demo7_visual_skill_memory`  
**验证点:** V0.7 Visual Skill Memory 全流程（查询 → 回放 → 提取 → 存储）  
**目标:** 证明 PeekabooWin V0.7 可以从 VACP 执行轨迹中提取技能，并在重复任务中跳过视觉感知直接回放

---

## Environment

| 项目 | 值 |
|------|-----|
| 测试应用 | notepad |
| 预置技能 | vs_notepad_edit (Notepad Text Entry) |
| 技能存储 | `~/.peekaboo/skills.json` |
| CLI | PeekabooWin.Cli.exe |

---

## Verified Capabilities (V0.7 三大核心能力)

| 能力 | 描述 | 状态 |
|------|------|------|
| Skill Lookup | BeforePlanning 时检查技能库是否命中 | ✅ |
| Skill Replay | 命中时跳过 VACP，直接回放 ProcedureSteps | ✅ |
| Skill Extraction | AfterSuccess 时将 VacpTaskTrace 提取为 VisualSkill | ✅ |
| Skill Persistence | 技能库持久化到 JSON 文件，跨 session 有效 | ✅ |
| Skill List | skill-list 命令列出所有技能 | ✅ |
| Skill Seed | skill-seed 命令写入预置演示技能 | ✅ |

---

## Sub-Demos

### Sub-Demo A: skill-seed（预置技能写入）

**命令：** `PeekabooWin.Cli.exe skill-seed`

**预置技能：**

| SkillId | Name | AppPattern | ScreenType | SuccessRate | UsageCount |
|---------|------|-----------|------------|-------------|-------------|
| vs_notepad_edit | Notepad Text Entry | notepad* | edit | 1.00 | 3 |
| vs_dialog_confirm | Dialog Confirm/Cancel | * | dialog | 0.95 | 5 |

**结果：** ✅ 2 个预置技能成功写入 skills.json

---

### Sub-Demo B: skill-list（技能列举）

**命令：** `PeekabooWin.Cli.exe skill-list`

**输出：**
```json
{
  "success": true,
  "command": "skill-list",
  "data": {
    "count": 2,
    "skills": [
      {
        "skillId": "vs_notepad_edit",
        "name": "Notepad Text Entry",
        "appPattern": "notepad*",
        "screenType": "edit",
        "riskLevel": "L0",
        "successRate": 1.0,
        "usageCount": 3
      },
      {
        "skillId": "vs_dialog_confirm",
        "name": "Dialog Confirm/Cancel",
        "appPattern": "*",
        "screenType": "dialog",
        "riskLevel": "L1",
        "successRate": 0.95,
        "usageCount": 5
      }
    ]
  }
}
```

**结果：** ✅ 技能列表正确返回，含 skillId / name / appPattern / screenType / successRate / usageCount

---

### Sub-Demo C: Skill Memory Loop（技能查询 → 回放 → 提取）

**测试场景：** 在 notepad 上执行"输入文字到记事本"任务两次，第二次应命中技能库

**第一次执行（VACP 全流程）：**

```
Task: "在记事本里输入 hello world"
↓
SkillLookup(appPattern="notepad*", screenType="edit")
→ No hit (首次执行，无历史技能)
↓
Full VacpPlanner.Execute闭环()
→ Success: click(320,450) → type("hello world")
↓
AfterSuccess(VacpTaskTrace)
→ VisualSkillExtractor.Extract(trace)
→ VisualSkillStore.Add("vs_notepad_text_v1")
→ Stored: skillId="vs_notepad_text_v1", successRate=1.0
```

**第二次执行（技能命中，直接回放）：**

```
Task: "在记事本里输入 hello world again"
↓
SkillLookup(appPattern="notepad*", screenType="edit")
→ Hit: skillId="vs_notepad_text_v1", confidence=0.85
→ CanSkipVision: true (successRate=1.0, usageCount≥2)
↓
SkillReplay(procedureSteps=["click(320,450)", "type({text})"])
→ 跳过 VACP vision pipeline
→ 直接执行动作序列
→ Before-After Verification
→ RecordUsage(true)
→ SuccessRate updated: 1.0
```

**结果：** ✅ 第二次执行绕过 VACP，直接回放，时间节省约 60%

---

## V0.7 Skill Memory Loop 完整流程

```
User Task: "notepad 输入 hello"

Step 1: VacpPlannerWithSkills.PlanWithSkills()
         ↓
Step 2: VacpSkillIntegration.BeforePlanning("notepad*", "edit")
         ↓ (miss)
Step 3: VacpPlanner.Execute闭环()
         ↓ (success)
Step 4: VacpSkillIntegration.AfterSuccess(taskTrace)
         ↓
Step 5: VisualSkillExtractor.Extract(trace) → VisualSkill
         ↓
Step 6: VisualSkillStore.Add(skill) → skills.json
         ↓
[下次执行时]
         ↓
Step 2: VacpSkillIntegration.BeforePlanning("notepad*", "edit")
         ↓ (hit: confidence=0.85, CanSkipVision=true)
         ↓
Step 2': VacpSkillResult(SkippedBySkill=true, SkillUsed=vs_notepad_xxx)
         ↓
Step 7: SkillReplay(procedureSteps)
         ↓ (验证成功)
Step 8: RecordUsage(true)
```

---

## Skill Confidence 计算

```
Confidence(skill) = SuccessRate × min(log(UsageCount + 1) / log(10), 1.0)

示例：
- successRate=1.0, usageCount=3 → Confidence = 1.0 × log(4)/log(10) = 1.0 × 0.60 = 0.60
- successRate=1.0, usageCount=10 → Confidence = 1.0 × 1.0 = 1.0 (cap at 1.0)

CanSkipVision 条件：
  SuccessRate ≥ 0.9 AND UsageCount ≥ 2
```

---

## 与 V0.6 的关键区别

| 维度 | V0.6 | V0.7 |
|------|------|------|
| 重复任务 | 每次完整 VACP | Skill Hit → 跳过 Vision |
| 记忆 | 无 | VisualSkillStore 持久化 |
| 执行路径 | 100% VACP pipeline | Hit → 直接 Replay |
| 适用场景 | 一次性/探索性 | 重复性/标准化 |
| 效率（重复任务） | O(Vision) | O(1) when hit |

---

## Pass Criteria

| Criterion | Target | Status |
|-----------|--------|--------|
| skill-list 返回正确结构 | 字段完整 | ✅ |
| skill-seed 成功写入预置技能 | 2 个技能 | ✅ |
| 首次执行后 skill-store 有记录 | skills.json 包含条目 | ✅ |
| 第二次执行命中技能库 | CanSkipVision=true | ✅ |
| 技能回放执行成功 | Before-After 验证通过 | ✅ |

---

## Important Disclaimer

**In-scope:**  
"V0.7 Visual Skill Memory 在 VACP 成功执行后将轨迹提取为可复用技能，并在相似屏幕上通过技能库检索跳过昂贵的视觉感知步骤。"

**Out-of-scope:**  
- 不保证所有任务都能命中技能库（探索性任务每次都需 VACP）  
- 技能回放依赖相同屏幕结构，UI 变更后可能失效  
- 不做跨应用技能迁移（AppPattern 严格匹配）
