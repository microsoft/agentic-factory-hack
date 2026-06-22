# Challenge 2: Building the Repair Planner Agent with GitHub Copilot

Welcome to Challenge 2!

In this challenge, you will create an intelligent **Repair Planner Agent** using .NET that generates comprehensive repair plans and work orders when faults are detected in tire manufacturing equipment. You'll leverage the `agentplanning` **GitHub Copilot** agent to guide your development and generate production-ready code.

**Expected Duration:** 45 minutes  
**Prerequisites**: [Challenge 0](../challenge-0/README.md) successfully completed

## 🎯 Objective

The goals for this challenge are:

- Pair program with **GitHub Copilot**
- Create a .NET agent using the **Foundry Agents SDK**
- Understand the similarities between a **Coding Assistant** (like GitHub Copilot) and a **Foundry Agent**—both use instructions, tools, and an LLM to accomplish tasks


## 🧭 Context and Background

![Challenge 2 Scenario](./images/challenge-2-scenario.png)

The **Repair Planner Agent** is the third component in our multi-agent system. After a fault has been diagnosed, this agent:

- Determines what repair tasks need to be performed
- Finds technicians with the required skills
- Checks parts inventory
- Creates a structured Work Order

You will implement the **Repair Planner Agent** as a .NET application that reads information about `Technicians` and `PartsInventory` from **Cosmos DB**. The final Work Order is also saved in **Cosmos DB**. The following diagram illustrates the target solution.

<img src="./images/challenge-2-target-solution.png" alt="Challenge 2 Target Solution" width="60%">

❶ The **Repair Planner** is a .NET console application with `Program.cs` as its entry point.

❷ The `RepairPlannerAgent.cs` class registers the agent and orchestrates calls to other services.

❸ The `CosmosDbService.cs` class encapsulates **Cosmos DB** data access.

❹ In this exercise, the mappings between faults and required skills/parts are done as a static mapping in `FaultMappingService.cs`. In a real-world application, this would be fetched from another system.

> [!TIP]
> This is just one way to structure the solution—there are many valid approaches! If you have experience with .NET, feel free to experiment with a different architecture (e.g., dependency injection, separate class libraries, or different layering patterns). The goal, though, is to have *GitHub Copilot* write the majority of the code while you provide guidance and review.

The following mappings are used in this exercise. The `agentplanning` agent already knows these mappings, so you don't need to copy them manually.

<details>
<summary>
Fault → Required Skills
</summary>
- `curing_temperature_excessive` → `tire_curing_press`, `temperature_control`, `instrumentation`, `electrical_systems`, `plc_troubleshooting`, `mold_maintenance`
- `curing_cycle_time_deviation` → `tire_curing_press`, `plc_troubleshooting`, `mold_maintenance`, `bladder_replacement`, `hydraulic_systems`, `instrumentation`
- `building_drum_vibration` → `tire_building_machine`, `vibration_analysis`, `bearing_replacement`, `alignment`, `precision_alignment`, `drum_balancing`, `mechanical_systems`
- `ply_tension_excessive` → `tire_building_machine`, `tension_control`, `servo_systems`, `precision_alignment`, `sensor_alignment`, `plc_programming`
- `extruder_barrel_overheating` → `tire_extruder`, `temperature_control`, `rubber_processing`, `screw_maintenance`, `instrumentation`, `electrical_systems`, `motor_drives`
- `low_material_throughput` → `tire_extruder`, `rubber_processing`, `screw_maintenance`, `motor_drives`, `temperature_control`
- `high_radial_force_variation` → `tire_uniformity_machine`, `data_analysis`, `measurement_systems`, `tire_building_machine`, `tire_curing_press`
- `load_cell_drift` → `tire_uniformity_machine`, `load_cell_calibration`, `measurement_systems`, `sensor_alignment`, `instrumentation`
- `mixing_temperature_excessive` → `banbury_mixer`, `temperature_control`, `rubber_processing`, `instrumentation`, `electrical_systems`, `mechanical_systems`
- `excessive_mixer_vibration` → `banbury_mixer`, `vibration_analysis`, `bearing_replacement`, `alignment`, `mechanical_systems`, `preventive_maintenance`
</details>

<details>
<summary>Fault → Required Parts</summary>
- `curing_temperature_excessive` → `TCP-HTR-4KW`, `GEN-TS-K400`
- `curing_cycle_time_deviation` → `TCP-BLD-800`, `TCP-SEAL-200`
- `building_drum_vibration` → `TBM-BRG-6220`
- `ply_tension_excessive` → `TBM-LS-500N`, `TBM-SRV-5KW`
- `extruder_barrel_overheating` → `EXT-HTR-BAND`, `GEN-TS-K400`
- `low_material_throughput` → `EXT-SCR-250`, `EXT-DIE-TR`
- `high_radial_force_variation` → (empty array)
- `load_cell_drift` → `TUM-LC-2KN`, `TUM-ENC-5000`
- `mixing_temperature_excessive` → `BMX-TIP-500`, `GEN-TS-K400`
- `excessive_mixer_vibration` → `BMX-BRG-22320`, `BMX-SEAL-DP`
</details>


### Using the Custom GitHub Copilot Agent (agentplanning)

**GitHub Copilot** custom agents in **VS Code** are reusable, task-specific chat personas. A custom agent bundles (1) a set of instructions (how **Copilot** should behave) and (2) an allowed set of tools (what **Copilot** can do). This makes it easy to switch into a consistent "mode" (for example, planning vs. implementation) without re-explaining context each time. In a workspace, custom agents are typically defined as `.agent.md` files under `.github/agents`.

This repository includes a specialized **GitHub Copilot** agent called `agentplanning` that knows:

- **Foundry Agents SDK** patterns (`Azure.AI.Projects` + `Microsoft.Agents.AI`)
- .NET and C# best practices
- **Cosmos DB** integration
- The fault→skills/parts mappings for this workshop

#### Agent-driven development workflow

Follow this workflow when using the agent planner:

1. **Ask the agent to plan** the component architecture
2. **Request code generation** with specific requirements
3. **Review and refine** the generated code
4. **Ask for improvements** or additional features
5. **Request tests** to validate functionality


## ✅ Tasks

> [!IMPORTANT]
> The outcome depends on which model GitHub Copilot uses. Larger models (`GPT-5.2`, `Claude Sonnet 4.5`) may handle more complex prompts. Smaller models work better with focused, single-file requests.

---

### Task 1: Project setup

Create a new empty .NET application that will host your agent.

```bash
# Navigate to challenge-2 directory
cd challenge-2

# Create a new console application
dotnet new console -n RepairPlanner

# Navigate into project
cd RepairPlanner
```

---  

### Task 2: Create RepairPlanner agent with `agentplanning`

Open **GitHub Copilot Chat** (Ctrl+Shift+I or Cmd+Shift+I) and select the `agentplanning` agent in the *Agents* dropdown.

<img src="./images/challenge-2-agentplanning-selection.png" alt="agentplanner selection" width="40%">

#### Task 2.1: Architecture planning

Start with the following prompt to understand the proposed setup for the **Repair Planner Agent**.

💬 Ask the agent:

```
I need to build a Repair Planner Agent in .NET for Challenge 2
using the Foundry Agents SDK. Can you explain the architecture?
```

#### Task 2.2: Create data models

Now let the agent create the data models.

💬 Ask the agent:

```
Create all data models for the Repair Planner Agent:
- DiagnosedFault (input from previous agent)
- Technician (with skills and availability) 
- Part (inventory items)
- WorkOrder (output with tasks)
- RepairTask (individual repair steps)
- WorkOrderPartUsage (parts needed)

Use dual JSON attributes for **Cosmos DB** compatibility.
```

<details>
<summary>📋 The agent will generate code similar to this structure</summary>

```csharp
using System.Text.Json.Serialization;
using Newtonsoft.Json;

public sealed class WorkOrder
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    
    // ... more properties
}
```

</details>

#### Task 2.3: Create `FaultMappingService`

Create a service for mapping between fault/skills and fault/parts. In this exercise, it will be a static mapping of values, but in a real-world scenario this would be fetched from a dedicated system.

💬 Ask the agent:

```
Create a FaultMappingService that maps fault types to required skills and parts using hardcoded dictionaries.
```

#### Task 2.4: Create `CosmosDbService`

Let's create the data access service.

💬 Ask the agent:

```
Create a CosmosDbService that:
- Queries available technicians by skills
- Fetches parts by part numbers  
- Creates work orders
Include error handling and logging.
```

<details>
<summary>📋 The agent will generate code similar to this:</summary>

```csharp
using Microsoft.Azure.Cosmos;
using RepairPlannerAgent.Models;

namespace RepairPlannerAgent.Services
{
    public class CosmosDbService
    {
        private readonly CosmosClient _client;
        private readonly Container _techniciansContainer;
        private readonly Container _partsContainer;
        private readonly Container _machinesContainer;
        private readonly Container _workOrdersContainer;

        public CosmosDbService(string endpoint, string key, string databaseName)
        {
            _client = new CosmosClient(endpoint, key);
            var database = _client.GetDatabase(databaseName);
            
            _techniciansContainer = database.GetContainer("Technicians");
            _partsContainer = database.GetContainer("PartsInventory");
            _machinesContainer = database.GetContainer("Machines");
            _workOrdersContainer = database.GetContainer("WorkOrders");
        }

        public async Task<List<Technician>> GetAvailableTechniciansWithSkillsAsync(List<string> requiredSkills)
        {
            // query logic 
        }

        public async Task<List<Part>> GetPartsInventoryAsync(List<string> partNumbers)
        {

            // query logic 
        }

        public async Task<string> CreateWorkOrderAsync(WorkOrder workOrder)
        {

            // query logic 
        }
    }
}
```
</details>

#### Task 2.5: Create the main agent

💬 Ask the agent:
```
Create the RepairPlannerAgent class that orchestrates the entire workflow
It should register the agent, determine required skills, query technicians and parts, 
and save the work order
```

<details>
<summary>📋 The agent will generate code similar to this:</summary>

```csharp
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;

public sealed class RepairPlannerAgent(
    AIProjectClient projectClient,
    CosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    private const string AgentName = "RepairPlannerAgent";
    
    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        var definition = new PromptAgentDefinition(model: modelDeploymentName)
        {
            Instructions = "..."
        };
        await projectClient.Agents.CreateAgentVersionAsync(
            AgentName, 
            new AgentVersionCreationOptions(definition), 
            ct);
    }
    
    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(DiagnosedFault fault, CancellationToken ct = default)
    {
        // 1. Get skills/parts from mapping
        // 2. Query Cosmos DB
        // 3. Build prompt and invoke agent
        // 4. Parse and save work order
    }
}
```

</details>

#### Task 2.6: Create the main program

Finally, let the `agentplanning` agent update `Program.cs` to initialize all services and run a sample fault.

💬 Ask the agent:

```
Update Program.cs to initialize all services, create a sample fault,
and demonstrate the repair planning workflow.

```

Your completed project could look similar to this:

```
RepairPlanner/
├── RepairPlanner.csproj
├── Program.cs
├── RepairPlannerAgent.cs
├── Models/
│   ├── DiagnosedFault.cs
│   ├── Technician.cs
│   ├── Part.cs
│   ├── WorkOrder.cs
│   ├── RepairTask.cs
│   └── WorkOrderPartUsage.cs
└── Services/
    ├── CosmosDbService.cs
    ├── CosmosDbOptions.cs
    └── FaultMappingService.cs
```

---

### Task 3: Test your agent

Try out your agent.

```bash
# Load environment variables
export $(cat ../../.env | xargs)

dotnet run
```

<details>
<summary>The output should look similar to this:</summary>

```bash
12:34:56 info: RepairPlannerAgent[0] Creating agent 'RepairPlannerAgent' with model 'gpt-4o'
12:34:57 info: RepairPlannerAgent[0] Agent version: abc123
12:34:57 info: RepairPlannerAgent[0] Planning repair for machine-001, fault=curing_temperature_excessive
12:34:58 info: CosmosDbService[0] Found 3 available technicians matching skills
12:34:58 info: CosmosDbService[0] Fetched 2 parts
12:34:58 info: RepairPlannerAgent[0] Invoking agent 'RepairPlannerAgent'
12:35:05 info: Program[0] Saved work order WO-2026-001 (id=xxx, status=new, assignedTo=tech-001)

{
  "id": "...",
  "workOrderNumber": "WO-2026-001",
  "machineId": "machine-001",
  "title": "Repair Curing Temperature Issue",
  ...
}

```
</details>
<br/>

🎉 Congratulations! You've built a **Repair Planner Agent** in .NET using **GitHub Copilot**.

## 🚀 Go Further

> [!NOTE]
> Finished early? These tasks are **optional** extras for exploration. Feel free to move on to the next challenge — you can always come back later!

<details>
<summary>Expose Cosmos DB queries as agent tools</summary>

In the current implementation, the agent orchestrates calls to **Cosmos DB** directly through the `CosmosDbService` class. An alternative approach is to expose the database queries as **tools** that the agent can invoke, letting the LLM decide when and how to use them.

**Why consider this?**

| Direct code orchestration | Tool-based approach |
|---------------------------|---------------------|
| You control the exact sequence of calls | Agent decides which tools to call and when |
| Predictable but rigid | More flexible—agent can adapt to different inputs |
| Good for well-defined workflows | Good when reasoning is needed to choose actions |

**How to implement:**

1. Define local function tools (similar to `get_thresholds` and `get_machine_data` in Challenge 1) that wrap your **Cosmos DB** queries:
   - `get_technicians_by_skills` — Returns available technicians matching required skills
   - `get_parts_inventory` — Returns parts by part numbers
   - `create_work_order` — Saves a new work order

2. Register these functions as tools when creating the agent

3. Update the agent's system prompt to explain when to use each tool

4. Let the agent decide which tools to call based on the diagnosed fault

**Taking it further:** Once you have local tools working, you could expose them as an **MCP server** (via **API Management** or a standalone server). This would allow the same tools to be shared across multiple agents and managed independently from the agent code—similar to how we exposed the Machine API in Challenge 1.

</details>

<details>
<summary>Enhance the Repair Planner Agent</summary>

Once the basic agent works, try adding:

```
Add priority calculation based on fault severity
```

```
Add better error handling for when no technicians are available
```

```
Add structured output using `AIJsonUtilities.CreateJsonSchema` 
and `ChatResponseFormat.ForJsonSchema` for type-safe responses
```

</details>

<details>
<summary>Add an MCP server to GitHub Copilot</summary>

In Challenge 1, you saw how agents in **Foundry Agent Service** can use MCP tools. **GitHub Copilot** in VS Code also supports MCP servers, letting you extend Copilot's capabilities with custom tools.

Try adding the **Microsoft Learn MCP server** to your **GitHub Copilot** setup:

1. Open the Command Palette (Ctrl+Shift+P or Cmd+Shift+P)
2. Run *MCP: Add Server*
3. Select *HTTP* as the server type
4. Enter `https://learn.microsoft.com/api/mcp` as the URL
5. Enter `microsoft-learn` as the server ID
6. Select *Workspace* to add the server to `.vscode/mcp.json`

This creates a `.vscode/mcp.json` file with the following configuration:

```json
{
    "servers": {
        "microsoft-learn": {
            "type": "http",
            "url": "https://learn.microsoft.com/api/mcp"
        }
    }
}
```

In **GitHub Copilot Chat**, you can now ask questions that leverage **Microsoft Learn** documentation—useful for questions about Azure services, .NET APIs, and other Microsoft technologies.

</details>

<details>
<summary>Create your own custom GitHub Copilot agent</summary>

In this challenge you used the `agentplanning` agent—a custom **GitHub Copilot** agent defined in `.github/agents/agentplanning.agent.md`. Try creating your own custom agent with a specific persona!

1. Create a new file in `.github/agents/` with a `.agent.md` extension
2. Define the agent's persona, instructions, and any tools it should use
3. Test it by selecting your new agent from the *Agents* dropdown in **GitHub Copilot Chat**

Here are two agent personas that would be useful in this hackathon:

**🧪 Agent Test Generator** (`agenttester.agent.md`)
- Persona: A test-driven development specialist for AI agents
- Purpose: Helps write unit tests, integration tests, and mock data for agent workflows
- Knowledge: xUnit/NUnit patterns, mocking **Cosmos DB** responses, testing async code, agent input/output validation
- Example prompt to handle: "Write unit tests for the CosmosDbService that mock the container responses"

**🗄️ Cosmos DB Helper** (`cosmoshelper.agent.md`)
- Persona: A database specialist focused on **Cosmos DB** best practices
- Purpose: Helps write efficient queries, design partition keys, and troubleshoot data access
- Knowledge: SQL API queries, indexing strategies, cross-partition queries, cost optimization
- Example prompt to handle: "Write a query to find all technicians with bearing_replacement skill who are available"

See [Custom agents in VS Code](https://code.visualstudio.com/docs/copilot/customization/custom-agents) for detailed documentation on creating custom agents.

</details>

## 🛠️ Troubleshooting and FAQ

<details>
<summary>Problem: Preview API warnings</summary>

Add this to your `RepairPlanner.csproj`:

```xml
<NoWarn>$(NoWarn);CA2252</NoWarn>
```

</details>

<details>
<summary>Problem: JSON parsing errors with numbers</summary>

LLMs sometimes return `"60"` instead of `60`. Use:

```csharp
NumberHandling = JsonNumberHandling.AllowReadingFromString
```

</details>

<details>
<summary>Problem: Cosmos DB errors</summary>

Ensure you're using both `[JsonPropertyName]` and `[JsonProperty]` attributes on models.

</details>

<details>
<summary>Question: Is there a finished example solution available?</summary>

Yes, there is a complete example solution in the `example-solution/` folder. However, it is **hidden from the VS Code file explorer** by default using filters in [.vscode/settings.json](../.vscode/settings.json).

**Why is it hidden?**  
The example solution is excluded to prevent **GitHub Copilot** from directly copying it as your solution — which would defeat the purpose of the exercise! The goal is to learn by building the agent yourself with Copilot's guidance, not to have Copilot retrieve a pre-made answer.

**How to view the solution:**  
If you want to compare your work or need a reference, you can remove or comment out the filter entries in [.vscode/settings.json](../.vscode/settings.json):

```json
{
    "files.exclude": {
        "example-solution": true,           // ← remove or set to false
        "**/example-solution/**": true
    },
    "search.exclude": {
        "example-solution": true,           // ← remove or set to false
        "**/example-solution/**": true
    }
}
```

After saving the file, the `example-solution` folder will appear in the Explorer and be included in search results.

</details>

## 🧠 Conclusion and Reflection


Let’s reflect on a few things

### C# vs Python

We used .NET (C#) in this challenge and Python in the previous one — both are **first-class** for building agents with modern AI/agent SDKs (including the **Foundry Agents SDK** patterns used in this workshop). Agent solutions quickly become application development (integration, data access, security, ops), so teams typically choose the language that best fits their existing stack and skills — Python often excels for rapid iteration, while .NET is common in larger enterprises for long-lived, well-governed services.

### GitHub Copilot instructions

This repo uses **VS Code** Copilot customization so the agent behaves consistently during the workshop.

> [!TIP]
> Using guided agents (clear instructions + constrained tools + repeatable steps) is an improvement over pure *vibe coding*, where solutions can drift, skip requirements, or become hard to review. A lightweight, guided approach keeps changes aligned with the goal and makes agent output easier to validate.


<img src="./images/challenge-2-copilot-instructions.png" alt="GitHub Copilot instructions" width="50%">


The diagram above illustrates how **GitHub Copilot** combines multiple inputs to generate contextual responses:

❶ **Your prompt** — The primary instruction you provide to the agent (e.g., "Create the RepairPlannerAgent class..."). This is your direct request that drives the conversation.

❷ **Custom instructions** — Two types of instruction files shape Copilot's behavior:
   - [copilot-instructions.md](../.github/copilot-instructions.md) — General workspace-wide instructions applied to all chat requests (SDK constraints, pinned package versions, environment variables, etc.)
   - [agentplanning.agent.md](../.github/agents/agentplanning.agent.md) — The specific role and persona for the `agentplanning` agent, discovered by **VS Code** from `.github/agents/*.agent.md` and selectable from the *Agents* dropdown

❸ **Workspace context** — Copilot examines files in your workspace to understand structure and data, such as `README.md`, `technicians.json`, `work-orders.json`, and your existing code files. This helps it generate code that fits your project.

❹ **Tools and data (MCP)** — Copilot can be equipped with additional tools exposed via the Model Context Protocol (MCP) to accomplish more complex tasks. **This is very similar to how our agents in [challenge 1](../challenge-1/README.md) were equipped with tools** — just as we gave the **Fault Diagnosis Agent** access to **Cosmos DB** queries and a knowledge base, you can extend Copilot with external data sources and capabilities.

### Taking it further with Spec Kit

In this challenge, you used custom instructions and a guided agent to build a single component. But what happens when you need to build entire features or complete applications with AI assistance?

[**Spec Kit**](https://github.com/github/spec-kit) is an open-source toolkit that takes AI-assisted development to the next level through **Spec-Driven Development (SDD)**. Instead of generating code directly from prompts, SDD flips the traditional approach: **specifications become executable**, directly generating working implementations rather than just guiding them.

**Why this matters for larger projects:**

| Traditional approach | Spec-Driven Development |
|---------------------|------------------------|
| Code is the source of truth | Specifications are the source of truth |
| Specs drift from implementation | Specs generate implementation |
| One-shot prompt → code | Multi-step: specify → plan → tasks → implement |
| Hard to review AI output | Structured artifacts at each stage |

**Spec Kit provides slash commands** that structure AI-assisted development:

- `/speckit.constitution` — Establish project principles and architectural guidelines
- `/speckit.specify` — Create functional specifications (focus on *what* and *why*, not *how*)
- `/speckit.plan` — Generate technical implementation plans with your chosen tech stack
- `/speckit.tasks` — Break down into actionable, parallelizable tasks
- `/speckit.implement` — Execute tasks according to the plan

**The key insight**: Specifications become living documents that generate code, tests, and documentation—rather than artifacts that are written once and ignored. When requirements change, you update the spec and regenerate, maintaining alignment between intent and implementation.

This approach is particularly valuable when:
- Building complete features or applications (not just single files)
- Working on teams where specifications need review and approval
- Maintaining long-lived projects where requirements evolve
- Wanting reproducible, auditable AI-assisted development

---

If you want to expand your knowledge on what we've covered in this challenge, have a look at the content below:

- [Custom agents in VS Code](https://code.visualstudio.com/docs/copilot/customization/custom-agents)
- [Custom instructions in VS Code](https://code.visualstudio.com/docs/copilot/customization/custom-instructions)
- [Use MCP servers in VS Code](https://code.visualstudio.com/docs/copilot/customization/mcp-servers)
- [Microsoft Learn MCP server](https://github.com/microsoftdocs/mcp)
- [Spec Kit on GitHub](https://github.com/github/spec-kit)
- [Spec-Driven Development blog post](https://developer.microsoft.com/blog/spec-driven-development-spec-kit)

---

**Next step:** [Challenge 3](../challenge-3/README.md) - Maintenance Scheduler & Parts Ordering Agents
