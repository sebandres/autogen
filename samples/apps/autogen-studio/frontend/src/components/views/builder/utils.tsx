import React, { useEffect } from "react";
import { IAgentFlowSpec, ILLMConfig, IModelConfig, ISkill } from "../../types";
import {
  GroupView,
  ControlRowView,
  ModelSelector,
  SkillSelector,
  Card,
} from "../../atoms";
import {
  checkAndSanitizeInput,
  fetchJSON,
  getServerUrl,
  sampleAgentConfig,
} from "../../utils";
import { Button, Input, Select, Slider, message } from "antd";
import TextArea from "antd/es/input/TextArea";
import {
  CodeBracketSquareIcon,
  RectangleGroupIcon,
  UserCircleIcon,
  UserGroupIcon,
} from "@heroicons/react/24/outline";
import { Agent } from "undici-types";
import { set } from "js-cookie";
import { appContext } from "../../../hooks/provider";

const AgentTypeView = ({
  setAgentType,
}: {
  setAgentType: (newAgentType: string) => void;
}) => {
  const iconClass = "h-6 w-6 inline-block ";
  const agentTypes = [
    {
      label: "User Proxy Agent",
      value: "userproxy",
      description: <>Typically represents the user and executes code. </>,
      icon: <UserCircleIcon className={iconClass} />,
    },
    {
      label: "Assistant Agent",
      value: "assistant",
      description: <>Plan and generate code to solve user tasks</>,
      icon: <CodeBracketSquareIcon className={iconClass} />,
    },
    {
      label: "GroupChat ",
      value: "groupchat",
      description: <>Manage group chat interactions</>,
      icon: <RectangleGroupIcon className={iconClass} />,
    },
  ];
  const [selectedAgentType, setSelectedAgentType] = React.useState<
    string | null
  >(null);

  const agentTypeRows = agentTypes.map((agentType: any, i: number) => {
    return (
      <li role="listitem" key={"agenttyperow" + i} className="w-36">
        <Card
          active={selectedAgentType === agentType.value}
          className="h-full p-2 cursor-pointer"
          title={<div className="  ">{agentType.label}</div>}
          onClick={() => {
            setSelectedAgentType(agentType.value);
            setAgentType(agentType.value);
          }}
        >
          <div style={{ minHeight: "35px" }} className="my-2   break-words">
            {" "}
            <div className="mb-2">{agentType.icon}</div>
            <span className="text-secondary  tex-sm">
              {" "}
              {agentType.description}
            </span>
          </div>
        </Card>
      </li>
    );
  });

  return (
    <>
      <div className="py-3">Select Agent Type</div>
      <ul className="inline-flex gap-2">{agentTypeRows}</ul>
    </>
  );
};

const AgentMainView = ({
  viewStatus,
  setViewStatus,
}: {
  viewStatus: Record<string, boolean>;
  setViewStatus: (newViewStatus: Record<string, boolean>) => void;
}) => {
  const [agentType, setAgentType] = React.useState<string | null>(null);
  const [flowSpec, setFlowSpec] = React.useState<IAgentFlowSpec | null>(null);

  useEffect(() => {
    console.log("agentType", agentType);
    if (agentType) {
      setFlowSpec(sampleAgentConfig(agentType));
    }
  }, [agentType]);
  return (
    <div>
      {!agentType && <AgentTypeView setAgentType={setAgentType} />}
      {agentType !== null && flowSpec && (
        <AgentConfigView flowSpec={flowSpec} setFlowSpec={setFlowSpec} />
      )}
    </div>
  );
};

const AgentConfigView = ({
  flowSpec,
  setFlowSpec,
}: {
  flowSpec: IAgentFlowSpec;
  setFlowSpec: (newFlowSpec: IAgentFlowSpec) => void;
}) => {
  const nameValidation = checkAndSanitizeInput(flowSpec?.config?.name);
  const [error, setError] = React.useState<any>(null);
  const [loading, setLoading] = React.useState<boolean>(false);
  const { user } = React.useContext(appContext);
  const serverUrl = getServerUrl();
  const createAgentUrl = `${serverUrl}/agents`;

  const onControlChange = (value: any, key: string) => {
    if (key === "llm_config") {
      if (value.config_list.length === 0) {
        value = false;
      }
    }
    const updatedFlowSpec = {
      ...flowSpec,
      config: { ...flowSpec.config, [key]: value },
    };

    setFlowSpec(updatedFlowSpec);
  };

  const createAgent = (agent: IAgentFlowSpec) => {
    setError(null);
    setLoading(true);
    // const fetch;
    agent.user_id = user?.email;

    const payLoad = {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(agent),
    };

    console.log("saving agent", agent);

    const onSuccess = (data: any) => {
      if (data && data.status) {
        message.success(data.message);
        console.log("agents", data.data);
        // setAgents(data.data);
      } else {
        message.error(data.message);
      }
      setLoading(false);
      // setNewAgent(sampleAgent);
    };
    const onError = (err: any) => {
      setError(err);
      message.error(err.message);
      setLoading(false);
    };
    fetchJSON(createAgentUrl, payLoad, onSuccess, onError);
  };

  return (
    <>
      <div>
        <GroupView
          title=<div className="px-2">{flowSpec?.config?.name}</div>
          className="mb-4 bg-primary "
        >
          <ControlRowView
            title="Agent Name"
            className="mt-4"
            description="Name of the agent"
            value={flowSpec?.config?.name}
            control={
              <>
                <Input
                  className="mt-2"
                  placeholder="Agent Name"
                  value={flowSpec?.config?.name}
                  onChange={(e) => {
                    onControlChange(e.target.value, "name");
                  }}
                />
                {!nameValidation.status && (
                  <div className="text-xs text-red-500 mt-2">
                    {nameValidation.message}
                  </div>
                )}
              </>
            }
          />

          <ControlRowView
            title="Agent Description"
            className="mt-4"
            description="Description of the agent, used by other agents
    (e.g. the GroupChatManager) to decide when to call upon this agent. (Default: system_message)"
            value={flowSpec.config.description || ""}
            control={
              <Input
                className="mt-2"
                placeholder="Agent Description"
                value={flowSpec.config.description || ""}
                onChange={(e) => {
                  onControlChange(e.target.value, "description");
                }}
              />
            }
          />

          <ControlRowView
            title="Max Consecutive Auto Reply"
            className="mt-4"
            description="Max consecutive auto reply messages before termination."
            value={flowSpec.config?.max_consecutive_auto_reply}
            control={
              <Slider
                min={1}
                max={flowSpec.type === "groupchat" ? 600 : 30}
                defaultValue={flowSpec.config.max_consecutive_auto_reply}
                step={1}
                onChange={(value: any) => {
                  onControlChange(value, "max_consecutive_auto_reply");
                }}
              />
            }
          />

          <ControlRowView
            title="Agent Default Auto Reply"
            className="mt-4"
            description="Default auto reply when no code execution or llm-based reply is generated."
            value={flowSpec.config.default_auto_reply || ""}
            control={
              <Input
                className="mt-2"
                placeholder="Agent Description"
                value={flowSpec.config.default_auto_reply || ""}
                onChange={(e) => {
                  onControlChange(e.target.value, "default_auto_reply");
                }}
              />
            }
          />

          <ControlRowView
            title="Human Input Mode"
            description="Defines when to request human input"
            value={flowSpec.config.human_input_mode}
            control={
              <Select
                className="mt-2 w-full"
                defaultValue={flowSpec.config.human_input_mode}
                onChange={(value: any) => {
                  onControlChange(value, "human_input_mode");
                }}
                options={
                  [
                    { label: "NEVER", value: "NEVER" },
                    // { label: "TERMINATE", value: "TERMINATE" },
                    // { label: "ALWAYS", value: "ALWAYS" },
                  ] as any
                }
              />
            }
          />
        </GroupView>
      </div>

      <div className="w-full mt-4 text-right">
        {" "}
        <Button
          className=" "
          type="primary"
          onClick={() => {
            createAgent(flowSpec);
          }}
        >
          Create Agent
        </Button>
      </div>
    </>
  );
};

export const AgentFlowSpecView = ({
  title = "Agent Specification",
  flowSpec,
  setFlowSpec,
}: {
  title: string;
  flowSpec: IAgentFlowSpec;
  setFlowSpec: (newFlowSpec: IAgentFlowSpec) => void;
  editMode?: boolean;
}) => {
  // Local state for the FlowView component
  const [localFlowSpec, setLocalFlowSpec] =
    React.useState<IAgentFlowSpec>(flowSpec);

  // Required to monitor localAgent updates that occur in GroupChatFlowSpecView and reflect updates.
  useEffect(() => {
    setLocalFlowSpec(flowSpec);
  }, [flowSpec]);

  const views = [
    {
      title: "Agent Configuration",
      slug: "agentconfig",
    },
    {
      title: "Agent Models",
      slug: "agentmodels",
    },
    {
      title: "Agent Skills",
      slug: "agentskills",
    },
  ];

  const [viewStatus, setViewStatus] = React.useState<Record<string, boolean>>(
    views.reduce((acc: Record<string, boolean>, view) => {
      acc[view.slug] = false;
      return acc;
    }, {})
  );

  const [agentType, setAgentType] = React.useState<string | null>(null);

  const RenderView = ({ viewIndex }: { viewIndex: number }) => {
    const view = views[viewIndex];
    switch (view.slug) {
      case "agentconfig":
        return (
          <AgentMainView
            viewStatus={viewStatus}
            setViewStatus={setViewStatus}
          />
        );
      case "agentmodels":
        return <div> .. models </div>;

      default:
        return <></>;
    }
  };

  useEffect(() => {
    console.log("view status changefd", viewStatus);
  }, [viewStatus]);

  const [currentViewIndex, setCurrentViewIndex] = React.useState<number>(0);
  return (
    <>
      <RenderView viewIndex={currentViewIndex} />
    </>
  );

  // return (
  // <>
  //   <div className="text-accent ">{title}</div>
  //   <GroupView
  //     title=<div className="px-2">{flowSpec?.config?.name}</div>
  //     className="mb-4 bg-primary  "
  //   >
  //     <ControlRowView
  //       title="Agent Name"
  //       className="mt-4"
  //       description="Name of the agent"
  //       value={flowSpec?.config?.name}
  //       control={
  //         <>
  //           <Input
  //             className="mt-2"
  //             placeholder="Agent Name"
  //             value={flowSpec?.config?.name}
  //             onChange={(e) => {
  //               onControlChange(e.target.value, "name");
  //             }}
  //           />
  //           {!nameValidation.status && (
  //             <div className="text-xs text-red-500 mt-2">
  //               {nameValidation.message}
  //             </div>
  //           )}
  //         </>
  //       }
  //     />

  //     <ControlRowView
  //       title="Agent Description"
  //       className="mt-4"
  //       description="Description of the agent, used by other agents
  //         (e.g. the GroupChatManager) to decide when to call upon this agent. (Default: system_message)"
  //       value={flowSpec.config.description || ""}
  //       control={
  //         <Input
  //           className="mt-2"
  //           placeholder="Agent Description"
  //           value={flowSpec.config.description || ""}
  //           onChange={(e) => {
  //             onControlChange(e.target.value, "description");
  //           }}
  //         />
  //       }
  //     />

  //     <ControlRowView
  //       title="Max Consecutive Auto Reply"
  //       className="mt-4"
  //       description="Max consecutive auto reply messages before termination."
  //       value={flowSpec.config?.max_consecutive_auto_reply}
  //       control={
  //         <Slider
  //           min={1}
  //           max={flowSpec.type === "groupchat" ? 600 : 30}
  //           defaultValue={flowSpec.config.max_consecutive_auto_reply}
  //           step={1}
  //           onChange={(value: any) => {
  //             onControlChange(value, "max_consecutive_auto_reply");
  //           }}
  //         />
  //       }
  //     />

  //     <ControlRowView
  //       title="Agent Default Auto Reply"
  //       className="mt-4"
  //       description="Default auto reply when no code execution or llm-based reply is generated."
  //       value={flowSpec.config.default_auto_reply || ""}
  //       control={
  //         <Input
  //           className="mt-2"
  //           placeholder="Agent Description"
  //           value={flowSpec.config.default_auto_reply || ""}
  //           onChange={(e) => {
  //             onControlChange(e.target.value, "default_auto_reply");
  //           }}
  //         />
  //       }
  //     />

  //     <ControlRowView
  //       title="Human Input Mode"
  //       description="Defines when to request human input"
  //       value={flowSpec.config.human_input_mode}
  //       control={
  //         <Select
  //           className="mt-2 w-full"
  //           defaultValue={flowSpec.config.human_input_mode}
  //           onChange={(value: any) => {
  //             onControlChange(value, "human_input_mode");
  //           }}
  //           options={
  //             [
  //               { label: "NEVER", value: "NEVER" },
  //               // { label: "TERMINATE", value: "TERMINATE" },
  //               // { label: "ALWAYS", value: "ALWAYS" },
  //             ] as any
  //           }
  //         />
  //       }
  //     />

  //     {llm_config && llm_config.config_list.length > 0 && (
  //       <ControlRowView
  //         title="System Message"
  //         className="mt-4"
  //         description="Free text to control agent behavior"
  //         value={flowSpec.config.system_message}
  //         control={
  //           <TextArea
  //             className="mt-2 w-full"
  //             value={flowSpec.config.system_message}
  //             rows={3}
  //             onChange={(e) => {
  //               onControlChange(e.target.value, "system_message");
  //             }}
  //           />
  //         }
  //       />
  //     )}
  //   </GroupView>
  // </>
  //   <></>
  // );
};
