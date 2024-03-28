import asyncio
from contextlib import asynccontextmanager
from datetime import datetime
import json
import os
import queue
import threading
import traceback
from typing import Any
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi import HTTPException
from openai import OpenAIError
from ..version import VERSION, APP_NAME


from ..models.db import Agent, DBResponseModel, Message, Model, Session, Skill, Workflow
from ..models.dbmanager import DBManager
from ..utils import md5_hash, init_app_folders, dbutils, test_model
from ..chatmanager import AutoGenChatManager, WebSocketConnectionManager


managers = {"chat": None}  # manage calls to autogen
# Create thread-safe queue for messages between api thread and autogen threads
message_queue = queue.Queue()
active_connections = []
active_connections_lock = asyncio.Lock()
websocket_manager = WebSocketConnectionManager(
    active_connections=active_connections,
    active_connections_lock=active_connections_lock,
)


def message_handler():
    while True:
        message = message_queue.get()
        print(
            "Active Connections: ",
            [client_id for _, client_id in websocket_manager.active_connections],
        )
        print("Current message connection id: ", message["connection_id"])
        for connection, socket_client_id in websocket_manager.active_connections:
            if message["connection_id"] == socket_client_id:
                asyncio.run(websocket_manager.send_message(message, connection))
        message_queue.task_done()


message_handler_thread = threading.Thread(target=message_handler, daemon=True)
message_handler_thread.start()


app_file_path = os.path.dirname(os.path.abspath(__file__))
folders = init_app_folders(app_file_path)
ui_folder_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ui")

db_path = os.path.join(folders["app_root"], "database.sqlite")
dbmanager = DBManager(engine_uri=f"sqlite:///{db_path}")


@asynccontextmanager
async def lifespan(app: FastAPI):
    print("***** App started *****")
    managers["chat"] = AutoGenChatManager(message_queue=message_queue)
    dbmanager.create_db_and_tables()

    yield
    # Close all active connections
    await websocket_manager.disconnect_all()
    print("***** App stopped *****")


app = FastAPI(lifespan=lifespan)


# allow cross origin requests for testing on localhost:800* ports only
app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "http://localhost:8000",
        "http://127.0.0.1:8000",
        "http://localhost:8001",
        "http://localhost:8081",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


api = FastAPI(root_path="/api")
# mount an api route such that the main route serves the ui and the /api
app.mount("/api", api)

app.mount("/", StaticFiles(directory=ui_folder_path, html=True), name="ui")
api.mount(
    "/files",
    StaticFiles(directory=folders["files_static_root"], html=True),
    name="files",
)


# manage websocket connections


def check_and_cast_datetime_fields(obj: Any) -> Any:
    if hasattr(obj, "created_at") and isinstance(obj.created_at, str):
        obj.created_at = str_to_datetime(obj.created_at)

    if hasattr(obj, "updated_at") and isinstance(obj.updated_at, str):
        obj.updated_at = str_to_datetime(obj.updated_at)

    return obj


def str_to_datetime(dt_str: str) -> datetime:
    if dt_str[-1] == "Z":
        # Replace 'Z' with '+00:00' for UTC timezone
        dt_str = dt_str[:-1] + "+00:00"
    return datetime.fromisoformat(dt_str)


def create_entity(model: Any, model_class: Any, filters: dict = None):
    """Create a new entity"""
    model = check_and_cast_datetime_fields(model)
    try:
        response: DBResponseModel = dbmanager.upsert(model)
        return response.model_dump()

    except Exception as ex_error:
        print(ex_error)
        return {
            "status": False,
            "message": f"Error occurred while creating {model_class.__name__}: "
            + str(ex_error),
        }


def list_entity(model_class: Any, filters: dict = None):
    """List all entities for a user"""
    try:
        entities = dbmanager.get(model_class, filters=filters, return_json=True)

        return {
            "status": True,
            "message": f"{model_class.__name__} retrieved successfully",
            "data": entities,
        }

    except Exception as ex_error:
        print(ex_error)
        return {
            "status": False,
            "message": f"Error occurred while retrieving {model_class.__name__}: "
            + str(ex_error),
        }


def delete_entity(model_class: Any, filters: dict = None):
    """Delete an entity"""
    try:
        status_message = dbmanager.delete(filters=filters, model_class=model_class)
        entities = dbmanager.get(
            model_class, filters={"user_id": filters["user_id"]}, return_json=True
        )
        return {
            "status": True,
            "message": f"Success - {model_class.__name__} {status_message}",
            "data": entities,
        }

    except Exception as ex_error:
        print(ex_error)
        return {
            "status": False,
            "message": f"Error occurred while deleting {model_class.__name__}: "
            + str(ex_error),
        }


@api.get("/skills")
async def list_skills(user_id: str):
    """List all skills for a user"""
    filters = {"user_id": user_id}
    return list_entity(Skill, filters=filters)


@api.post("/skills")
async def create_skill(skill: Skill):
    """Create a new skill"""
    filters = {"user_id": skill.user_id}
    return create_entity(skill, Skill, filters=filters)


@api.delete("/skills/delete")
async def delete_skill(skill_id: int, user_id: str):
    """Delete a skill"""
    filters = {"id": skill_id, "user_id": user_id}
    return delete_entity(Skill, filters=filters)


@api.get("/models")
async def list_models(user_id: str):
    """List all models for a user"""
    filters = {"user_id": user_id}
    return list_entity(Model, filters=filters)


@api.post("/models")
async def create_model(model: Model):
    """Create a new model"""
    return create_entity(model, Model)


@api.post("/models/test")
async def test_model_endpoint(model: Model):
    """Test a model"""
    try:
        response = test_model(model)
        return {
            "status": True,
            "message": "Model tested successfully",
            "data": response,
        }
    except (OpenAIError, Exception) as ex_error:
        return {
            "status": False,
            "message": "Error occurred while testing model: " + str(ex_error),
        }


@api.delete("/models/delete")
async def delete_model(model_id: int, user_id: str):
    """Delete a model"""
    filters = {"id": model_id, "user_id": user_id}
    return delete_entity(Model, filters=filters)


@api.get("/agents")
async def list_agents(user_id: str):
    """List all agents for a user"""
    filters = {"user_id": user_id}
    return list_entity(Agent, filters=filters)


@api.post("/agents")
async def create_agent(agent: Agent):
    """Create a new agent"""
    return create_entity(agent, Agent)


@api.delete("/agents/delete")
async def delete_agent(agent_id: int, user_id: str):
    """Delete an agent"""
    filters = {"id": agent_id, "user_id": user_id}
    return delete_entity(Agent, filters=filters)


@api.get("/workflows")
async def list_workflows(user_id: str):
    """List all workflows for a user"""
    filters = {"user_id": user_id}
    return list_entity(Workflow, filters=filters)


@api.post("/workflows")
async def create_workflow(workflow: Workflow):
    """Create a new workflow"""
    return create_entity(workflow, Workflow)


@api.delete("/workflows/delete")
async def delete_workflow(workflow_id: int, user_id: str):
    """Delete a workflow"""
    filters = {"id": workflow_id, "user_id": user_id}
    return delete_entity(Workflow, filters=filters)


@api.get("/sessions")
async def list_sessions(user_id: str):
    """List all sessions for a user"""
    filters = {"user_id": user_id}
    return list_entity(Session, filters=filters)


@api.post("/sessions")
async def create_session(session: Session):
    """Create a new session"""
    return create_entity(session, Session)


@api.delete("/sessions/delete")
async def delete_session(session_id: int, user_id: str):
    """Delete a session"""
    filters = {"id": session_id, "user_id": user_id}
    return delete_entity(Session, filters=filters)


@api.get("/messages")
async def list_messages(user_id: str, session_id: str):
    """List all messages for a user"""
    filters = {"user_id": user_id, "session_id": session_id}
    return list_entity(Message, filters=filters)


@api.post("/messages")
async def create_message(message: Message):
    """Create a new message"""

    user_message_history = dbmanager.get(
        Message,
        filters={"user_id": message.user_id, "session_id": message.session_id},
        return_json=True,
    )

    # save incoming message
    dbmanager.upsert(message)
    user_dir = os.path.join(
        folders["files_static_root"], "user", md5_hash(message.user_id)
    )
    os.makedirs(user_dir, exist_ok=True)

    workflow = dbmanager.get(Workflow, filters={"id": message.workflow_id})[0]

    try:
        agent_response: Message = managers["chat"].chat(
            message=message,
            history=user_message_history,
            user_dir=user_dir,
            flow_config=workflow,
            connection_id=message.connection_id,
        )

        messages = dbmanager.upsert(agent_response)
        response = {
            "status": True,
            "message": "Success - Message processed successfully",
            "data": messages,
        }
        return response
    except Exception as ex_error:
        print(traceback.format_exc())
        return {
            "status": False,
            "message": "Error occurred while processing message: " + str(ex_error),
        }


@api.get("/version")
async def get_version():
    return {
        "status": True,
        "message": "Version retrieved successfully",
        "data": {"version": VERSION},
    }


# websockets


async def process_socket_message(data: dict, websocket: WebSocket, client_id: str):
    print(f"Client says: {data['type']}")
    if data["type"] == "user_message":
        user_message = Message(**data["data"])
        response = await create_message(user_message)
        response_socket_message = {
            "type": "agent_response",
            "data": response,
            "connection_id": client_id,
        }
        await websocket_manager.send_message(response_socket_message, websocket)


@api.websocket("/ws/{client_id}")
async def websocket_endpoint(websocket: WebSocket, client_id: str):
    await websocket_manager.connect(websocket, client_id)
    try:
        while True:
            data = await websocket.receive_json()
            await process_socket_message(data, websocket, client_id)
    except WebSocketDisconnect:
        print(f"Client #{client_id} is disconnected")
        await websocket_manager.disconnect(websocket)
