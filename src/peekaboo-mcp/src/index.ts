#!/usr/bin/env node

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

const API_BASE = process.env.PEEKABOO_API_URL || "http://localhost:8025";

async function apiGet(path: string): Promise<any> {
  const res = await fetch(`${API_BASE}${path}`);
  return res.json();
}

async function apiPost(path: string, body: Record<string, unknown>): Promise<any> {
  const res = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return res.json();
}

const server = new Server(
  { name: "peekaboo-win", version: "0.15.0" },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: [
    {
      name: "peekaboo_list_windows",
      description: "List all visible windows on the desktop. Optionally filter by keyword.",
      inputSchema: {
        type: "object" as const,
        properties: {
          keyword: { type: "string", description: "Optional keyword to filter windows by title" },
        },
      },
    },
    {
      name: "peekaboo_focus_window",
      description: "Bring a window to the foreground by its title keyword.",
      inputSchema: {
        type: "object" as const,
        properties: {
          title: { type: "string", description: "Window title keyword to match" },
        },
        required: ["title"],
      },
    },
    {
      name: "peekaboo_screenshot",
      description: "Capture a screenshot of the entire screen or a specific window.",
      inputSchema: {
        type: "object" as const,
        properties: {
          window: { type: "string", description: "Optional window title keyword to capture (omit for full screen)" },
          out_path: { type: "string", description: "Optional output file path for the screenshot" },
        },
      },
    },
    {
      name: "peekaboo_click",
      description: "Click the mouse at specified screen coordinates.",
      inputSchema: {
        type: "object" as const,
        properties: {
          x: { type: "number", description: "X coordinate on screen" },
          y: { type: "number", description: "Y coordinate on screen" },
        },
        required: ["x", "y"],
      },
    },
    {
      name: "peekaboo_type",
      description: "Type text into the currently focused element.",
      inputSchema: {
        type: "object" as const,
        properties: {
          text: { type: "string", description: "Text to type" },
        },
        required: ["text"],
      },
    },
    {
      name: "peekaboo_press_key",
      description: "Press a single key by name (e.g. 'enter', 'tab', 'escape').",
      inputSchema: {
        type: "object" as const,
        properties: {
          key: { type: "string", description: "Key name (e.g. 'enter', 'tab', 'escape', 'space')" },
        },
        required: ["key"],
      },
    },
    {
      name: "peekaboo_hotkey",
      description: "Press a keyboard shortcut / hotkey combination (e.g. 'ctrl+a', 'alt+f4').",
      inputSchema: {
        type: "object" as const,
        properties: {
          keys: { type: "string", description: "Hotkey combination (e.g. 'ctrl+a', 'alt+f4', 'ctrl+shift+s')" },
        },
        required: ["keys"],
      },
    },
    {
      name: "peekaboo_ocr",
      description: "Perform OCR on the screen or a specific window. Optionally search for text and return its position.",
      inputSchema: {
        type: "object" as const,
        properties: {
          window: { type: "string", description: "Optional window title keyword to capture before OCR" },
          text: { type: "string", description: "Optional text to search for within OCR results" },
          lang: { type: "string", description: "OCR language (default: 'chi_sim+eng')" },
        },
      },
    },
    {
      name: "peekaboo_inspect",
      description: "Inspect the UI Automation tree of a window. Returns element names, types, and bounding rectangles.",
      inputSchema: {
        type: "object" as const,
        properties: {
          window: { type: "string", description: "Window title keyword to inspect" },
          max_depth: { type: "number", description: "Maximum tree depth (default: 5)" },
        },
        required: ["window"],
      },
    },
    {
      name: "peekaboo_agent_run",
      description: "Run an autonomous agent task. The agent parses natural language into UI actions and executes them with risk gating and verification.",
      inputSchema: {
        type: "object" as const,
        properties: {
          task: { type: "string", description: "Natural language task description (e.g. 'open notepad and type hello')" },
          max_steps: { type: "number", description: "Maximum number of steps (default: 5)" },
          dry_run: { type: "boolean", description: "If true, parse only without executing (default: false)" },
          context: { type: "string", description: "Optional additional context for the task" },
        },
        required: ["task"],
      },
    },
    {
      name: "peekaboo_skill_search",
      description: "Search for reusable visual skills that match a given task description.",
      inputSchema: {
        type: "object" as const,
        properties: {
          task: { type: "string", description: "Task description to match skills against" },
          app_pattern: { type: "string", description: "Optional application pattern filter (e.g. 'notepad')" },
        },
        required: ["task"],
      },
    },
    {
      name: "peekaboo_skill_list",
      description: "List all stored visual skills.",
      inputSchema: {
        type: "object" as const,
        properties: {},
      },
    },
    {
      name: "peekaboo_skill_replay",
      description: "Replay a stored visual skill by its ID. Supports dry-run mode.",
      inputSchema: {
        type: "object" as const,
        properties: {
          skill_id: { type: "string", description: "ID of the skill to replay" },
          dry_run: { type: "boolean", description: "If true, simulate without executing (default: true)" },
          execute: { type: "boolean", description: "If true, actually execute the skill steps (default: false)" },
        },
        required: ["skill_id"],
      },
    },
    {
      name: "peekaboo_risk_evaluate",
      description: "Evaluate the risk level of a planned action before execution.",
      inputSchema: {
        type: "object" as const,
        properties: {
          action: { type: "string", description: "Action name (e.g. 'click', 'type', 'hotkey')" },
          target: { type: "string", description: "Target element or text" },
          window: { type: "string", description: "Window title keyword" },
        },
        required: ["action"],
      },
    },
    {
      name: "peekaboo_execute",
      description: "Execute a raw PeekabooWin CLI command with arguments. Use for commands not covered by specific tools.",
      inputSchema: {
        type: "object" as const,
        properties: {
          command: { type: "string", description: "CLI command name (e.g. 'list-windows', 'find', 'click-element')" },
          args: {
            type: "object",
            description: "Command arguments as key-value pairs",
            additionalProperties: { type: "string" },
          },
        },
        required: ["command"],
      },
    },
  ],
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    switch (name) {
      case "peekaboo_list_windows": {
        const query = args?.keyword ? `?keyword=${encodeURIComponent(args.keyword as string)}` : "";
        const result = await apiGet(`/windows${query}`);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_focus_window": {
        const result = await apiPost("/focus-window", { title: args!.title });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_screenshot": {
        const body: Record<string, unknown> = {};
        if (args?.window) body.window = args.window;
        if (args?.out_path) body.out_path = args.out_path;
        const result = await apiPost("/screenshot", body);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_click": {
        const result = await apiPost("/click", { x: args!.x, y: args!.y });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_type": {
        const result = await apiPost("/type", { text: args!.text });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_press_key": {
        const result = await apiPost("/press", { key: args!.key });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_hotkey": {
        const result = await apiPost("/hotkey", { keys: args!.keys });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_ocr": {
        const body: Record<string, unknown> = {};
        if (args?.window) body.window = args.window;
        if (args?.text) body.text = args.text;
        if (args?.lang) body.lang = args.lang;
        const result = await apiPost("/ocr", body);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_inspect": {
        const query = new URLSearchParams({ window: args!.window as string });
        if (args?.max_depth) query.set("max_depth", String(args.max_depth));
        const result = await apiGet(`/inspect?${query.toString()}`);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_agent_run": {
        const body: Record<string, unknown> = { task: args!.task };
        if (args?.max_steps) body.max_steps = args.max_steps;
        if (args?.dry_run !== undefined) body.dry_run = args.dry_run;
        if (args?.context) body.context = args.context;
        const result = await apiPost("/agent", body);
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_skill_search": {
        const result = await apiPost("/api/v1/skill/search", {
          task: args!.task,
          app_pattern: args?.app_pattern,
        });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_skill_list": {
        const result = await apiGet("/api/v1/skills/list");
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_skill_replay": {
        const result = await apiPost("/api/v1/skill/replay", {
          skill_id: args!.skill_id,
          dry_run: args?.dry_run ?? true,
          execute: args?.execute ?? false,
        });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_risk_evaluate": {
        const result = await apiPost("/api/v1/risk/evaluate", {
          action: args!.action,
          target: args?.target,
          window: args?.window,
        });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      case "peekaboo_execute": {
        const result = await apiPost("/execute", {
          command: args!.command,
          args: args?.args ?? {},
        });
        return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
      }

      default:
        return {
          content: [{ type: "text", text: `Unknown tool: ${name}` }],
          isError: true,
        };
    }
  } catch (error: any) {
    return {
      content: [
        {
          type: "text",
          text: `Error calling ${name}: ${error.message}. Make sure PeekabooWin API Server is running at ${API_BASE}`,
        },
      ],
      isError: true,
    };
  }
});

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((err) => {
  console.error("Fatal error:", err);
  process.exit(1);
});
