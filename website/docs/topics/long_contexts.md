# Handling Long Context Conversations with Transform Messages

Why do we need to handle long contexts? The problem arises from several constraints and requirements:

1. Token limits: LLMs have token limits that restrict the amount of textual data they can process. If we exceed these limits, we may encounter errors or incur additional costs. By preprocessing the chat history, we can ensure that we stay within the acceptable token range.

2. Context relevance: As conversations progress, retaining the entire chat history may become less relevant or even counterproductive. Keeping only the most recent and pertinent messages can help the LLMs focus on the most crucial context, leading to more accurate and relevant responses.

3. Efficiency: Processing long contexts can consume more computational resources, leading to slower response times.

## Transform Messages Capability

The `TransformMessages` capability is designed to modify incoming messages before they are processed by the LLM agent. This can include limiting the number of messages, truncating messages to meet token limits, and more.

### Installation and Setup

````{=mdx}
:::info Requirements
Install `pyautogen`:
```bash
pip install pyautogen
```

For more information, please refer to the [installation guide](/docs/installation/).
:::
````

```python
import os
import pprint
import copy
import re

import autogen
from autogen.agentchat.contrib.capabilities import transform_messages, transforms
from typing import Dict, List

config_list = autogen.config_list_from_json(
    env_or_file="OAI_CONFIG_LIST",
)
# Define your llm config
llm_config = {"config_list": config_list}
```

```{=mdx}
:::tip
Learn more about configuring LLMs for agents [here](/docs/topics/llm_configuration).
:::
```

```python
# Define your agent; the user proxy and an assistant
assistant = autogen.AssistantAgent(
    "assistant",
    llm_config=llm_config,
)
user_proxy = autogen.UserProxyAgent(
    "user_proxy",
    human_input_mode="NEVER",
    is_termination_msg=lambda x: "TERMINATE" in x.get("content", ""),
    max_consecutive_auto_reply=10,
)
```

### Example 1: Limiting the Total Number of Messages

Consider a scenario where you want to limit the context history to only the most recent messages to maintain efficiency and relevance. You can achieve this with the MessageHistoryLimiter transformation:

```python
# Limit the message history to the 3 most recent messages
max_msg_transfrom = transforms.MessageHistoryLimiter(max_messages=3)

messages = [
    {"role": "user", "content": "hello"},
    {"role": "assistant", "content": [{"type": "text", "text": "there"}]},
    {"role": "user", "content": "how"},
    {"role": "assistant", "content": [{"type": "text", "text": "are you doing?"}]},
    {"role": "user", "content": "very very very very very very long string"},
]

processed_messages = max_msg_transfrom.apply_transform(copy.deepcopy(messages))
pprint.pprint(processed_messages)
```

```console
[{'content': 'how', 'role': 'user'},
{'content': [{'text': 'are you doing?', 'type': 'text'}], 'role': 'assistant'},
{'content': 'very very very very very very long string', 'role': 'user'}]
```

By applying the `MessageHistoryLimiter`, we can see that we limited the context history to the 3 most recent messages.

### Example 2: Limiting the Number of Tokens

To adhere to token limitations, use the `MessageTokenLimiter` transformation. This limits tokens per message and the total token count across all messages:

```python
# Limit the token limit per message to 3 tokens
token_limit_transform = transforms.MessageTokenLimiter(max_tokens_per_message=3)

processed_messages = token_limit_transform.apply_transform(copy.deepcopy(messages))

pprint.pprint(processed_messages)
```

```console
[{'content': 'hello', 'role': 'user'},
{'content': [{'text': 'there', 'type': 'text'}], 'role': 'assistant'},
{'content': 'how', 'role': 'user'},
{'content': [{'text': 'are you doing', 'type': 'text'}], 'role': 'assistant'},
{'content': 'very very very', 'role': 'user'}]
```

We can see that we can limit the number of tokens to 3, which is equivalent to 3 words in this instance.

### Example 3: Combining Multiple Transformations Using the `TransformMessages` Capability

Let's test these transforms with AutoGen's agents. We will see that the agent without the capability to handle long context will result in an error, while the agent with the capability will have no issues.

```python
llm_config = {
    "config_list": [{"model": "gpt-3.5-turbo", "api_key": os.environ.get("OPENAI_API_KEY")}],
}

assistant_base = autogen.AssistantAgent(
    "assistant",
    llm_config=llm_config,
)

assistant_with_context_handling = autogen.AssistantAgent(
    "assistant",
    llm_config=llm_config,
)
# suppose this capability is not available
context_handling = transform_messages.TransformMessages(
    transforms=[
        transforms.MessageHistoryLimiter(max_messages=10),
        transforms.MessageTokenLimiter(max_tokens=1000, max_tokens_per_message=50),
    ]
)

context_handling.add_to_agent(assistant_with_context_handling)

user_proxy = autogen.UserProxyAgent(
    "user_proxy",
    human_input_mode="NEVER",
    is_termination_msg=lambda x: "TERMINATE" in x.get("content", ""),
    code_execution_config={
        "work_dir": "coding",
        "use_docker": False,
    },
    max_consecutive_auto_reply=2,
)

# suppose the chat history is large
# Create a very long chat history that is bound to cause a crash
# for gpt 3.5
for i in range(1000):
    # define a fake, very long messages
    assitant_msg = {"role": "assistant", "content": "test " * 1000}
    user_msg = {"role": "user", "content": ""}

    assistant_base.send(assitant_msg, user_proxy, request_reply=False, silent=True)
    assistant_with_context_handling.send(assitant_msg, user_proxy, request_reply=False, silent=True)
    user_proxy.send(user_msg, assistant_base, request_reply=False, silent=True)
    user_proxy.send(user_msg, assistant_with_context_handling, request_reply=False, silent=True)

try:
    user_proxy.initiate_chat(assistant_base, message="plot and save a graph of x^2 from -10 to 10", clear_history=False)
except Exception as e:
    print("Encountered an error with the base assistant")
    print(e)
    print("\n\n")

try:
    user_proxy.initiate_chat(
        assistant_with_context_handling, message="plot and save a graph of x^2 from -10 to 10", clear_history=False
    )
except Exception as e:
    print(e)
```

### Example 4: Creating Custom Transformations to Handle Sensitive Content

You can use the `MessageTransform` protocol to create custom message transformations that redact sensitive data from the chat history. This is particularly useful when you want to ensure that sensitive information, such as API keys, passwords, or personal data, is not exposed in the chat history or logs.

Now, we will create a custom message transform to detect any OpenAI API key and redact it.

```python
# The transform must adhere to transform_messages.MessageTransform protocol.
class MessageRedact:
    def __init__(self):
        self._openai_key_pattern = r"sk-([a-zA-Z0-9]{48})"
        self._replacement_string = "REDACTED"

    def apply_transform(self, messages: List[Dict]) -> List[Dict]:
        temp_messages = copy.deepcopy(messages)

        for message in temp_messages:
            if isinstance(message["content"], str):
                message["content"] = re.sub(self._openai_key_pattern, self._replacement_string, message["content"])
            elif isinstance(message["content"], list):
                for item in message["content"]:
                    if item["type"] == "text":
                        item["text"] = re.sub(self._openai_key_pattern, self._replacement_string, item["text"])
        return temp_messages

assistant_with_redact = autogen.AssistantAgent(
    "assistant",
    llm_config=llm_config,
    max_consecutive_auto_reply=1,
)
# suppose this capability is not available
redact_handling = transform_messages.TransformMessages(transforms=[MessageRedact()])

redact_handling.add_to_agent(assistant_with_redact)

user_proxy = autogen.UserProxyAgent(
    "user_proxy",
    human_input_mode="NEVER",
    max_consecutive_auto_reply=1,
)

messages = [
    {"content": "api key 1 = sk-7nwt00xv6fuegfu3gnwmhrgxvuc1cyrhxcq1quur9zvf05fy"},  # Don't worry, randomly generated
    {"content": [{"type": "text", "text": "API key 2 = sk-9wi0gf1j2rz6utaqd3ww3o6c1h1n28wviypk7bd81wlj95an"}]},
]

for message in messages:
    user_proxy.send(message, assistant_with_redact, request_reply=False, silent=True)

result = user_proxy.initiate_chat(
    assistant_with_redact, message="What are the two API keys that I just provided", clear_history=False

```

````console
 user_proxy (to assistant):



 What are the two API keys that I just provided



 --------------------------------------------------------------------------------

 assistant (to user_proxy):



 To retrieve the two API keys you provided, I will display them individually in the output.



 Here is the first API key:

 ```python

 # Display the first API key

 print("API key 1 =", "REDACTED")

 ```



 Here is the second API key:

 ```python

 # Display the second API key

 print("API key 2 =", "REDACTED")

 ```



 Please run the code snippets to see the API keys. After that, I will mark this task as complete.



 --------------------------------------------------------------------------------



 >>>>>>>> EXECUTING CODE BLOCK 0 (inferred language is python)...



 >>>>>>>> EXECUTING CODE BLOCK 1 (inferred language is python)...

 user_proxy (to assistant):



 exitcode: 0 (execution succeeded)

 Code output:

 API key 1 = REDACTED

 API key 2 = REDACTED
````