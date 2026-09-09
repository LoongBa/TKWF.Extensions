using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>功能管理扩展专用用户类型（扩展不知消费方 UserInfo 类型，ADR42 D4）。</summary>
public class FeatureManagementUserInfo : SimpleUserInfo
{
    public FeatureManagementUserInfo() : base() { }
}
