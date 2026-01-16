"""
ActionTrace 数据模型定义

符合 Unity 端 EditorEvent 和 EventStore 的数据结构
提供类型安全的序列化和反序列化支持

设计原则：
- Python 端只负责数据验证和类型转换
- 业务逻辑（摘要生成、语义分析）由 C# 端完成
- 配置从 C# 端获取，不使用硬编码默认值
"""
from typing import Any, Optional
from pydantic import BaseModel, Field


class EditorEvent(BaseModel):
    """
    Unity 编辑器事件数据模型

    对应 Unity 端 MCPForUnity.Editor.ActionTrace.Core.EditorEvent

    字段由 C# 端生成，Python 端只负责验证和类型转换

    C# 端 JsonProperty 映射：
    - Sequence → "sequence"
    - TimestampUnixMs → "timestamp_unix_ms"
    - Type → "type"
    - TargetId → "target_id"
    - Payload → "payload"
    - PrecomputedSummary → "precomputed_summary"
    - IsDehydrated → "is_dehydrated"
    """
    sequence: int = Field(..., description="单调递增的序列号，用于排序")
    timestamp_unix_ms: int = Field(..., description="UTC 时间戳（毫秒）")
    type: str = Field(..., description="事件类型标识符")
    target_id: str = Field(..., description="目标标识符（实例ID、资源GUID或文件路径）")
    payload: Optional[dict[str, Any]] = Field(None, description="事件载荷，包含额外的上下文数据")
    precomputed_summary: Optional[str] = Field(None, description="C# 端生成的预计算摘要")
    is_dehydrated: bool = Field(False, description="事件载荷是否已脱水（内存优化）")

    # 可选的语义字段（当 include_semantics=True 时返回）
    importance_score: Optional[float] = Field(None, description="重要性评分 (0.0-1.0)")
    importance_category: Optional[str] = Field(None, description="重要性类别 (low/medium/high/critical)")
    inferred_intent: Optional[str] = Field(None, description="推断的操作意图")

    # 可选的上下文字段（当 include_context=True 时返回）
    has_context: Optional[bool] = Field(None, description="是否有关联的上下文信息")
    context: Optional[dict[str, Any]] = Field(None, description="上下文详情")

    class Config:
        """Pydantic 配置"""
        populate_by_name = True  # 允许使用别名


class EventQueryResult(BaseModel):
    """
    事件查询结果

    对应 C# 端返回的响应格式
    """
    events: list[EditorEvent] = Field(default_factory=list, description="事件列表")
    total_count: int = Field(0, description="总事件数")
    current_sequence: Optional[int] = Field(None, description="当前序列号")
    has_more: Optional[bool] = Field(None, description="是否还有更多事件")
    schema_version: Optional[str] = Field(None, description="Schema 版本")
    context_mapping_count: Optional[int] = Field(None, description="上下文映射数量")


class EventStatistics(BaseModel):
    """
    事件统计信息

    由 Python 端基于 C# 返回的数据计算
    """
    total_events: int = Field(default=0, description="总事件数")
    events_by_type: dict[str, int] = Field(default_factory=dict, description="按类型分组的事件计数")
    oldest_event_sequence: Optional[int] = Field(default=None, description="最旧事件的序列号")
    newest_event_sequence: Optional[int] = Field(default=None, description="最新事件的序列号")
    time_span_ms: Optional[int] = Field(default=None, description="时间跨度（毫秒）")
    dehydrated_count: int = Field(default=0, description="已脱水的事件数量")
    hydrated_count: int = Field(default=0, description="未脱水的事件数量")


class ActionTraceSettings(BaseModel):
    """
    ActionTrace 系统设置

    从 C# 端获取的实际配置，不使用硬编码默认值
    """
    schema_version: str = Field(..., description="Schema 版本")

    # 事件过滤
    min_importance_for_recording: float = Field(..., description="记录事件的最小重要性阈值")
    disabled_event_types: list[str] = Field(default_factory=list, description="禁用的事件类型列表")

    # 事件合并
    enable_event_merging: bool = Field(..., description="是否启用事件合并")
    merge_window_ms: int = Field(..., description="事件合并时间窗口（毫秒）")

    # 存储限制
    max_events: int = Field(..., description="最大事件数量")
    hot_event_count: int = Field(..., description="热事件数量（保留完整载荷）")

    # 事务聚合
    transaction_window_ms: int = Field(..., description="事务聚合时间窗口（毫秒）")

    # 当前存储状态
    current_sequence: int = Field(..., description="当前序列号")
    total_events_stored: int = Field(..., description="存储的事件总数")
    context_mapping_count: int = Field(..., description="上下文映射数量")


class TransactionInfo(BaseModel):
    """
    事务信息

    表示一组相关的操作序列（聚合模式）
    """
    start_sequence: int = Field(..., description="起始事件序列号")
    end_sequence: int = Field(..., description="结束事件序列号")
    summary: str = Field(..., description="事务摘要")
    event_count: int = Field(..., description="包含的事件数量")
    duration_ms: int = Field(..., description="持续时间（毫秒）")
    tool_call_id: Optional[str] = Field(None, description="触发此事务的工具调用ID")
    triggered_by_tool: Optional[bool] = Field(None, description="是否由工具触发")
