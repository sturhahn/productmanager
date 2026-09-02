# Onboarding

## Goals

1) I would like a new user to be able to onboard this product quickly.
2) We can assume that the user will use an agent to do this onboarding
3) Therefore, our goal is to provide an agent with a task list and conversion information so the agent can handle loading the instance quickly and efficiently.

## Concepts

- The README.md should point to the agentic onboarding guide.
- The onboarding guide should clearly state the mandatory and optional steps for an onboarding.
- The guide should clearly state the requirements for running the software, including verification checks of software versions, and running servers (w/ correct schema), etc.
- The guide should follow the rule of progressive disclosure - assume that subagents will be spawned to handle each task, so the initial guide should be structured to allow for the orchestrator agent to understand the tasks, ask questions of the user about how to get the required data, then spin off subagents for each data set.
- Each data set should have a dedicated file that states data requirements, units for conversion, and other relevant information to allow an agent to construct and import the data via the CLI.
